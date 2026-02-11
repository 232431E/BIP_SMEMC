using BIP_SMEMC.Models;
using BIP_SMEMC.Models.SupabaseModels;
using BIP_SMEMC.Services;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Vml;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics; // Required for Debug.WriteLine
using System.Text.RegularExpressions;

namespace BIP_SMEMC.Controllers
{
    public class ExpenseController : Controller
    {
        private readonly Supabase.Client _supabase;
        private readonly FinanceService _financeService;
        private readonly DebtService _debtService;

        private int _skippedEmptyCount = 0;
        public ExpenseController(Supabase.Client supabase, FinanceService financeService, DebtService debtService)
        {
            _supabase = supabase;
            _financeService = financeService;
            _debtService =  debtService;
        }
        public async Task<IActionResult> Index()
        {
            var userEmail = User.Identity?.Name ?? "dummy@sme.com";
            // 1. DYNAMIC ANCHOR: Find when the user last had activity
            var latestDate = await _financeService.GetLatestTransactionDate(userEmail);
            // 2. RANGE: Pull the full year containing that latest date
            var startDate = latestDate.AddMonths(-11).AddDays(1 - latestDate.Day); 
            // Start of month, 1 year back
            var endDate = latestDate;

            var allTrans = await _financeService.GetUserTransactions(userEmail, startDate, latestDate);
            var catRes = await _supabase.From<CategoryModel>().Get();
            var categories = catRes.Models;

            var expenseRoot = categories.FirstOrDefault(c => c.Name.Equals("Expense", StringComparison.OrdinalIgnoreCase));

            var expensesOnly = allTrans
                .Where(t => t.Debit > 0)
                .Select(t => {
                    var leafCat = categories.FirstOrDefault(c => c.Id == t.CategoryId);
                    CategoryModel tier1 = leafCat;
                    while (tier1 != null && tier1.ParentId != expenseRoot?.Id && tier1.ParentId != 0)
                        tier1 = categories.FirstOrDefault(c => c.Id == tier1.ParentId);

                    t.CategoryName = leafCat?.Name ?? "Uncategorized";
                    t.ParentCategoryName = tier1?.Name ?? "General Expense";
                    return t;
                })
                .Where(t => !t.ParentCategoryName.Contains("Ordinary") && !t.CategoryName.Contains("Jan - Dec"))
                .OrderBy(x => x.Date).ToList();

            // --- NEW DEBUG STATEMENTS ---
            Debug.WriteLine("================ DASHBOARD DATA AUDIT ================");
            Debug.WriteLine($"[DEBUG] Total Valid Expenses Found: {expensesOnly.Count}");

            var monthlyStats = expensesOnly
                .GroupBy(t => t.Date.Month)
                .Select(g => new { Month = g.Key, Count = g.Count(), Sum = g.Sum(x => x.Debit) })
                .OrderBy(x => x.Month);

            foreach (var stat in monthlyStats)
            {
                string mName = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(stat.Month);
                Debug.WriteLine($"[DEBUG] {mName}: {stat.Count} records | Total Spent: ${stat.Sum:N2}");
            }
            Debug.WriteLine("======================================================");

            var model = new ExpenseManagementViewModel
            {
                Transactions = expensesOnly,
                ExpenseSubCategories = categories.Where(c => c.ParentId == expenseRoot?.Id).ToList(),
                TotalExpenses = expensesOnly.Sum(t => t.Debit)
            };
            return View(model);
        }
        [HttpPost]
        public async Task<IActionResult> ImportExcel(IFormFile file)
        {
            if (file == null || file.Length == 0) return BadRequest();
            var model = new ExpenseManagementViewModel();


            // Alternative Header Mapping (Synonyms)
            string[] nameSynonyms = { "name", "vendor", "payee" };
            string[] typeSynonyms = { "type" };
            string[] dateSynonyms = { "date", "trans", "posted", "day" };
            string[] descSynonyms = { "desc", "memo", "details", "narrative", "name" };
            string[] debitSynonyms = { "debit", "amt", "amount", "spent", "dr", "out" };
            string[] creditSynonyms = { "credit", "received", "in", "cr" };
            string[] catSynonyms = { "cat", "account", "class", "type" };
            string[] balSynonyms = { "balance", "bal", "running" };

            // ENSURE USER EXISTS IN SUPABASE
            var userEmail = User.Identity?.Name ?? "dummy@sme.com";
            var userCheck = await _supabase.From<UserModel>().Where(u => u.Email == userEmail).Get();
            if (!userCheck.Models.Any())
            {
                // Creating the missing user so we can safely link transactions to them
                var newUser = new UserModel
                {
                    Email = userEmail,
                    FullName = "Demo User",
                    PasswordHash = "placeholder" // Required because your SQL says NOT NULL
                };
                await _supabase.From<UserModel>().Insert(newUser);
            }

            try
            {
                using (var stream = new MemoryStream())
                {
                    await file.CopyToAsync(stream);
                    using (var workbook = new XLWorkbook(stream))
                    {
                        // --- PASS 1: INTUITIVE CATEGORY BUILDING ---
                        // 1. Fetch Categories ONCE at the start to share between all sheets
                        var allCatsRes = await _supabase.From<CategoryModel>().Get();
                        var masterCategoryCache = allCatsRes.Models.ToList();

                        var allSummaries = new List<AnnualSummaryModel>();
                        foreach (var sheet in workbook.Worksheets)
                        {
                            string sName = sheet.Name.ToUpper();
                            if (sName.Contains("PL") || sName.Contains("P&L") || sName.Contains("BS") || sName.Contains("BALANCE"))
                            {
                                string defaultType = (sName.Contains("PL") || sName.Contains("P&L")) ? "Income" : "Asset";
                                // Pass the master cache into the method so it updates in real-time
                                var sheetSummaries = await ProcessCategoriesAndSummaries(sheet, userEmail, defaultType, masterCategoryCache);
                                allSummaries.AddRange(sheetSummaries);
                            }
                        }

                        // 2. Final Batch Save for Summaries with Strict Uniqueness Check
                        if (allSummaries.Any())
                        {
                            await SyncSummaries(allSummaries);
                        }

                        // 3. REFRESH CACHE FOR GL: Use the updated master list
                        var allCats = await _supabase.From<CategoryModel>().Get();
                        var categoryCache = masterCategoryCache
                            .OrderByDescending(c => c.ParentId.HasValue)
                            .GroupBy(c => c.Name.ToLower().Trim().Replace(" ", ""))
                            .ToDictionary(g => g.Key, g => g.First().Id);
                        foreach (var c in allCats.Models)
                        {
                            string pName = allCats.Models.FirstOrDefault(p => p.Id == c.ParentId)?.Name?.ToLower().Trim() ?? "";
                            string cName = c.Name.ToLower().Trim();

                            // Key 1: Path based (ParentName + CategoryName)
                            string pathKey = (pName + cName).Replace(" ", "");
                            if (!categoryCache.ContainsKey(pathKey)) categoryCache[pathKey] = c.Id;

                            // Key 2: Name based (Only if not already set by a more specific path)
                            string nameKey = cName.Replace(" ", "");
                            if (!categoryCache.ContainsKey(nameKey)) categoryCache[nameKey] = c.Id;
                        }

                        IXLWorksheet targetSheet = null;
                        var headerMap = new Dictionary<string, int>();
                        // LOGIC: Search all sheets for the one containing "Date" and "Debit"
                        foreach (var sheet in workbook.Worksheets)
                        {
                            var potentialHeaderRow = sheet.RowsUsed().FirstOrDefault(r =>
                                r.Cells().Any(c => c.GetString().ToLower().Contains("date")));

                            if (potentialHeaderRow != null)
                            {
                                targetSheet = sheet;
                                foreach (var cell in potentialHeaderRow.Cells())
                                {
                                    string h = cell.GetString().ToLower().Trim();
                                    if (dateSynonyms.Any(s => h.Contains(s))) headerMap["date"] = cell.Address.ColumnNumber;
                                    if (nameSynonyms.Any(s => h == s)) headerMap["name"] = cell.Address.ColumnNumber; // Exact match for Name
                                    if (typeSynonyms.Any(s => h == s)) headerMap["type"] = cell.Address.ColumnNumber;
                                    if (debitSynonyms.Any(s => h.Contains(s))) headerMap["amt"] = cell.Address.ColumnNumber;
                                    if (creditSynonyms.Any(s => h.Contains(s))) headerMap["credit"] = cell.Address.ColumnNumber;
                                    if (descSynonyms.Any(s => h.Contains(s))) headerMap["desc"] = cell.Address.ColumnNumber;
                                    if (catSynonyms.Any(s => h.Contains(s))) headerMap["cat"] = cell.Address.ColumnNumber;
                                    if (balSynonyms.Any(s => h.Contains(s))) headerMap["balance"] = cell.Address.ColumnNumber;
                                }
                                // Break if we found at least Date and one Amount column
                                if (headerMap.ContainsKey("date") && (headerMap.ContainsKey("amt") || headerMap.ContainsKey("credit"))) break;
                            }
                        }

                        // PREVIEW LOGIC: If headers missing or for debugging, capture 10 rows
                        if (targetSheet == null || !headerMap.ContainsKey("date"))
                        {
                            var debugSheet = workbook.Worksheet(1);
                            model.ErrorMessage = "Required columns not detected. Previewing first sheet below:";
                            model.ExcelHeaders = debugSheet.Row(1).Cells().Select(c => c.GetString()).ToList();
                            foreach (var row in debugSheet.RowsUsed().Take(11))
                                model.ExcelPreviewRows.Add(row.Cells().Select(c => c.GetString()).ToList());

                            // Reload original data to keep the page functional
                            var latestDate = await _financeService.GetLatestTransactionDate(userEmail);
                            var currentData = await _financeService.GetUserTransactions(userEmail, latestDate.AddMonths(-1), latestDate);
                            model.Transactions = currentData;
                            return View("Index", model);
                        }

                        // SAVE LOGIC: Extract and save
                        // --- PASS 2: PROCESS GL TRANSACTIONS ---
                        var glSheet = workbook.Worksheets.FirstOrDefault(w => w.Name.ToUpper().Contains("GL") || w.Name.ToUpper().Contains("LEDGER"));
                        if (glSheet != null)
                        {
                            // A. Process Transactions (Existing)
                            await ProcessGLTransactions(glSheet, userEmail, masterCategoryCache);

                            // B. Process Payroll (Existing)
                            await SyncEmployeesAndPayroll(userEmail, masterCategoryCache);

                            // C. PROCESS DEBT (NEW)
                            // This ensures that immediately after transactions are in, we check for loans
                            await _debtService.SyncDebtsFromTransactions(userEmail);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                model.ErrorMessage = $"Import Failed: {ex.Message}";
                Debug.WriteLine($"IMPORT ERROR: {ex}");
            }
            return RedirectToAction("Index");
        }

        private async Task SyncSummaries(List<AnnualSummaryModel> summaries)
        {
            Debug.WriteLine($"[TRACE] Syncing {summaries.Count} Summaries...");
            var dbRes = await _supabase.From<AnnualSummaryModel>().Get();
            var lookup = dbRes.Models.ToDictionary(s => $"{s.UserId?.ToLower().Trim()}|{s.CategoryId}|{s.Year}", s => s.Id);

            var normalized = summaries.GroupBy(s => new { U = s.UserId.ToLower().Trim(), C = s.CategoryId, Y = s.Year })
                .Select(g => {
                    var first = g.First();
                    string key = $"{g.Key.U}|{g.Key.C}|{g.Key.Y}";
                    var item = new AnnualSummaryModel
                    {
                        UserId = g.Key.U,
                        CategoryId = g.Key.C,
                        Year = g.Key.Y,
                        AnnualTotalActual = g.Sum(x => x.AnnualTotalActual),
                        ReportType = first.ReportType
                    };
                    if (lookup.TryGetValue(key, out int? id)) item.Id = id;
                    return item;
                }).ToList();

            var toUpdate = normalized.Where(x => x.Id.HasValue).ToList();
            var toInsert = normalized.Where(x => !x.Id.HasValue).ToList();

            if (toUpdate.Any()) await _supabase.From<AnnualSummaryModel>().Upsert(toUpdate);
            // .Insert() is the specific command that allows Postgres to use SERIAL for the ID
            if (toInsert.Any()) await _supabase.From<AnnualSummaryModel>().Insert(toInsert);
        }
        private async Task ProcessGLTransactions(IXLWorksheet sheet, string email, List<CategoryModel> cache)
        {
            Debug.WriteLine($"[TRACE] STARTING SMART IMPORT: {sheet.Name}");

            // 1. DYNAMIC CATEGORY ANCHORS
            int utilitiesId = cache.FirstOrDefault(c => c.Name.Contains("Utilities"))?.Id ??
                              cache.FirstOrDefault(c => c.Name.Contains("Electricity"))?.Id ?? 0; 
            int payrollOtherId = cache.FirstOrDefault(c => c.Name.Contains("Payroll Liabilities - Other"))?.Id ?? 276;
            int salariesWagesId = cache.FirstOrDefault(c => c.Name.Contains("Salaries & wages"))?.Id ?? 133;
            int staffMealsId = cache.FirstOrDefault(c => c.Name.Contains("Staff meals"))?.Id ?? 144;
            int bankInterestId = cache.FirstOrDefault(c => c.Name.Contains("Loan interest"))?.Id ?? 114;
            int finePenaltyId = cache.FirstOrDefault(c => c.Name.Contains("Fine and Penalty"))?.Id ?? 93;
            int cpfId = cache.FirstOrDefault(c => c.Name.Contains("CPF contribution") || c.Name.Contains("CPF payable"))?.Id ?? 128;
            int adminExpId = cache.FirstOrDefault(c => c.Name.Contains("Administrative expenses"))?.Id ?? 85;
            int payrollId = cache.FirstOrDefault(c => c.Name.Contains("Salaries") || c.Name.Contains("Wages"))?.Id ?? 0;

            // 2. MAPPING DICTIONARIES
            var govEntities = new[] { "lta", "iras", "spf", "cpfb", "fwl", "mom", "acra", "ura", "comc" };
            var bankEntities = new[] { "dbs", "ocbc", "uob", "maybank", "cimb", "standard chartered", "loan", "interest", "finance", "bridging" };
            var foodKeywords = new[] { "mcdonald", "food", "lunch", "dinner", "restaurant", "catering", "kitchen", "cafe", "pantry", "yum cha", "coffee" };
            var companySuffixes = new[] { "pte", "ltd", "corp", "inc", "engineering", "services", "technologies", "logistic", "express", "towing", "repair" };

            // Fuzzy Search Index
            var searchIndex = cache.Select(c => new { Id = (int)c.Id, Key = Regex.Replace(c.Name.ToLower(), @"[^a-z0-9]", "") }).ToList();

            string noisePattern = @"\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)('\d{2})?|(\d{2,4})|being|adjustment|advance|tenure|years\b";
            var matches = new List<Dictionary<string, object>>();
            var skippedLogs = new List<string>();
            _skippedEmptyCount = 0; 
            var headerRow = sheet.RowsUsed().FirstOrDefault(r => r.Cells().Any(c => c.GetString().ToLower().Contains("date"))); var cols = new Dictionary<string, int>();
            foreach (var cell in headerRow.Cells())
            {
                string h = cell.GetString().ToLower().Trim();
                if (h.Contains("date")) cols["date"] = cell.Address.ColumnNumber;
                if (h.Contains("desc") || h.Contains("memo")) cols["desc"] = cell.Address.ColumnNumber;
                if (h.Contains("name")) cols["name"] = cell.Address.ColumnNumber;
                if (h.Contains("debit") || h.Contains("amt")) cols["debit"] = cell.Address.ColumnNumber;
                if (h.Contains("credit")) cols["credit"] = cell.Address.ColumnNumber;
                if (h.Contains("bal")) cols["bal"] = cell.Address.ColumnNumber;
            }

            var dataRows = sheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber()).ToList();
            Debug.WriteLine($"[DEBUG] Total Excel Rows to process: {dataRows.Count}");
            foreach (var row in dataRows)
            {
                int rowNum = row.RowNumber();
                try
                {
                    // 1. Safe Number Parsing (Prevents InvalidCastException)
                    string rawName = row.Cell(cols["name"]).GetString().Trim();
                    string rawDesc = row.Cell(cols["desc"]).GetString().Trim();
                    string fullDesc = $"{rawName} {rawDesc}".Trim();
                    decimal dr = ParseSafeDecimal(row.Cell(cols.GetValueOrDefault("debit", 0)));
                    decimal cr = ParseSafeDecimal(row.Cell(cols.GetValueOrDefault("credit", 0)));
                    decimal bal = ParseSafeDecimal(row.Cell(cols.GetValueOrDefault("bal", 0)));

                    if (string.IsNullOrEmpty(fullDesc))
                    {
                        _skippedEmptyCount++;
                        continue;
                    }

                    // 1. DATE VALIDATION
                    var dateCell = row.Cell(cols["date"]);
                    DateTime transDate;
                    bool dateParsed = false;
                    if (dateCell.Value.IsDateTime)
                    {
                        transDate = dateCell.GetDateTime();
                        dateParsed = true;
                    }
                    else
                    {
                        dateParsed = DateTime.TryParse(dateCell.GetString(), out transDate);
                    }

                    if (!dateParsed)
                    {
                        skippedLogs.Add($"Row {rowNum}: [DATE ERROR] Value: '{dateCell.GetString()}'");
                        continue;
                    }
                    string cleanFull = Regex.Replace(fullDesc.ToLower(), @"[^a-z0-9]", "");
                    string nameOnlyClean = Regex.Replace(rawName.ToLower(), @"\b(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)('\d{2})?|(\d{2,4})\b", "").Trim();
                    int? finalCatId = null;

                    // --- PRIORITY 0: UTILITIES (New Requirement) ---
                    if (cleanFull.Contains("spservices") || cleanFull.Contains("spbill") ||
                        cleanFull.Contains("electricity") || cleanFull.Contains("waterbill") ||
                        cleanFull.Contains("utilities"))
                    {
                        finalCatId = utilitiesId; // Assign to Utilities
                    }
                    // --- HEURISTIC ENGINE ---
                    // --- PRIORITY 1: KEYWORD SEARCH (Gov, Bank, Food) ---
                    if (!finalCatId.HasValue)
                    {
                        if (govEntities.Any(g => cleanFull.Contains(g))) finalCatId = finePenaltyId;
                        else if (bankEntities.Any(b => cleanFull.Contains(b))) finalCatId = bankInterestId;
                        else if (foodKeywords.Any(f => cleanFull.Contains(f))) finalCatId = staffMealsId;
                    }
                    // --- PRIORITY 2: DATABASE CATEGORY MATCH ---
                    if (!finalCatId.HasValue)
                    {
                        var match = searchIndex.FirstOrDefault(x => cleanFull.Contains(x.Key));
                        if (match != null) finalCatId = match.Id;
                    }

                    // --- PRIORITY 3: HUMAN NAME HEURISTIC (2-5 words, no business keywords) ---
                    if (!finalCatId.HasValue)
                    {
                        string[] nameParts = nameOnlyClean.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        bool isBusiness = companySuffixes.Any(s => cleanFull.Contains(s));

                        if (!isBusiness && !Regex.IsMatch(nameOnlyClean, @"\d") && nameParts.Length >= 2 && nameParts.Length <= 5)
                            finalCatId = payrollOtherId;
                        else if (cleanFull.Contains("salary") || cleanFull.Contains("pay") || cleanFull.Contains("salaries"))
                            finalCatId = salariesWagesId;
                    }
                    if (finalCatId.HasValue)
                    {
                        matches.Add(new Dictionary<string, object> {
                            { "user_id", email.ToLower() },
                            { "date", transDate.ToString("yyyy-MM-dd") }, // FIX: String prevents loop
                            { "description", fullDesc },
                            { "debit", ParseSafeDecimal(row.Cell(cols["debit"])) },
                            { "credit", ParseSafeDecimal(row.Cell(cols["credit"])) },
                            { "category_id", finalCatId.Value },
                            { "type", "Expense" },
                            { "tran_month", transDate.Month },
                            { "tran_year", transDate.Year }
                        });
                    }else
                    {
                        skippedLogs.Add($"Row {row.RowNumber()}: [REJECTED] '{fullDesc}'");
                    }
                }catch (Exception ex)
                {
                    skippedLogs.Add($"Row {rowNum}: [CRASH] Error: {ex.Message}");
                }
            }

            // --- REPORTING PHASE ---
            Debug.WriteLine("============= IMPORT REJECTION REPORT =============");
            Debug.WriteLine($"Total Rows: {dataRows.Count}");
            Debug.WriteLine($"Matched:    {matches.Count}| Skipped Empty: {_skippedEmptyCount}");
            Debug.WriteLine($"Rejected:   {skippedLogs.Count}");
            Debug.WriteLine("--------------------------------------------------");
            // Print the first 50 failures for investigation
            foreach (var log in skippedLogs.Take(50))
            {
                Debug.WriteLine(log);
            }
            if (skippedLogs.Count > 50) Debug.WriteLine($"... and {skippedLogs.Count - 50} more failures.");
            Debug.WriteLine("==================================================");

            // --- UPLOAD PHASE (Transactions) ---
            if (matches.Any())
            {
                Debug.WriteLine($"[DB SYNC] Inserting {matches.Count} transactions...");

                var finalBatch = matches.GroupBy(t => $"{t["user_id"]}|{t["date"]}|{t["description"].ToString().ToLower()}|{t["debit"]}").Select(g => g.First()).ToList();
                Debug.WriteLine($"[DB SYNC] Attempting to insert {finalBatch.Count} transactions...");
                for (int i = 0; i < finalBatch.Count; i += 1000)
                {
                    var chunk = finalBatch.Skip(i).Take(1000).ToList();
                    await _supabase.Rpc("import_transactions_batch", new Dictionary<string, object> { { "json_data", chunk } });
                }
                await SyncEmployeesAndPayroll(email, cache);
            }

            //await SyncEmployeesFromTransactions(email, cache); //outdated and not relevant
            Debug.WriteLine($"[TRACE] IMPORT FINISHED. Matched: {matches.Count}, Skipped: {skippedLogs.Count}");
        }
        // 2. NEW FUNCTION: Strict Employee & Payroll Sync
        // Inside ExpenseController.cs
        private async Task SyncEmployeesAndPayroll(string email, List<CategoryModel> cache)
        {
            Debug.WriteLine("=== [START] PAYROLL SYNC ===");

            // 1. FETCH TRANSACTIONS
            var payrollIds = cache.Where(c => c.Name.Contains("Payroll") || c.Name.Contains("Salaries") || c.Name.Contains("Wages")).Select(c => c.Id).ToList();
            var allTrans = await _financeService.GetUserTransactions(email, new DateTime(2020, 1, 1), DateTime.Now);
            var payrollTrans = allTrans.Where(t => payrollIds.Contains(t.CategoryId)).ToList();

            Debug.WriteLine($"[TRACE] Processing {payrollTrans.Count} payroll records...");

            var employeesToUpsert = new List<EmployeeModel>();

            // 2. LOAD EXISTING EMPLOYEES
            var existingDb = await _supabase.From<EmployeeModel>().Where(e => e.UserId == email).Get();
            // Map Name -> UUID (Database Primary Key)
            var validEmployees = existingDb.Models.ToDictionary(e => e.Name, e => e.Id);

            // Counter for custom IDs (e.g., SME001)
            int idCounter = existingDb.Models.Count + 1;
            string prefix = "EMP";
            try { prefix = email.Split('@')[1].Split('.')[0].ToUpper().Substring(0, 3); } catch { }

            string[] noiseList = { "ntuc", "fairprice", "tissue", "breaking", "offer", "household", "cpf", "levy", "rental", "transport", "grab", "claims", "allowance", "repayment" };

            // 3. IDENTIFY NEW EMPLOYEES
            foreach (var t in payrollTrans)
            {
                var match = Regex.Match(t.Description, @"^([A-Za-z\s]+?)\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    string name = match.Groups[1].Value.Trim();
                    name = Regex.Replace(name, @"^(Being|Payment|Adv|Advance|Salary)\s+", "", RegexOptions.IgnoreCase).Trim();

                    if (name.Length < 3 || noiseList.Any(n => name.ToLower().Contains(n))) continue;

                    if (!validEmployees.ContainsKey(name) && !employeesToUpsert.Any(e => e.Name == name))
                    {
                        // Generate UUID in C# so we can use it immediately for logs
                        string newUuid = Guid.NewGuid().ToString();

                        var newEmp = new EmployeeModel
                        {
                            Id = newUuid,
                            UserId = email,
                            Name = name,
                            EmployeeId = $"{prefix}{idCounter:D3}", // Custom ID (SME001)
                            Email = $"{name.Replace(" ", ".").ToLower()}@placeholder.com", // Required field
                            Position = "Employee", // Required field
                            Age = 30, // Required field
                            MonthlySalary = t.Debit, // Required field
                            OvertimeHourlyRate = 0, // Required field
                            CPFRate = 20.00m,
                            DateJoined = DateTime.Now
                        };

                        employeesToUpsert.Add(newEmp);
                        validEmployees[name] = newUuid; // Add to local map for the next step
                        idCounter++;
                    }
                }
            }

            // 4. UPSERT EMPLOYEES
            if (employeesToUpsert.Any())
            {
                try
                {
                    // Ensure your EmployeeModel has [PrimaryKey("id", true)] if you want to send this specific UUID
                    await _supabase.From<EmployeeModel>().Upsert(employeesToUpsert);
                    Debug.WriteLine($"[DB] Inserted {employeesToUpsert.Count} new employees.");
                }
                catch (Exception ex) { Debug.WriteLine($"[DB ERROR - EMPLOYEES] {ex.Message}"); }
            }

            // 5. BUILD PAYROLL LOGS
            var newPayrollLogs = new List<PayrollLogModel>();
            var existingLogs = await _supabase.From<PayrollLogModel>().Select("trans_id").Get();
            var processedTransIds = new HashSet<int>(existingLogs.Models.Select(x => x.TransId));

            foreach (var t in payrollTrans)
            {
                if (t.Id.HasValue && processedTransIds.Contains(t.Id.Value)) continue;

                var match = Regex.Match(t.Description, @"^([A-Za-z\s]+?)\s+(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string name = match.Groups[1].Value.Trim();
                    name = Regex.Replace(name, @"^(Being|Payment|Adv|Advance|Salary)\s+", "", RegexOptions.IgnoreCase).Trim();

                    if (validEmployees.ContainsKey(name))
                    {
                        string empUuid = validEmployees[name]; // This is the UUID

                        decimal netPay = t.Debit; // Transaction is usually Net Pay
                        if (netPay == 0) continue;

                        decimal estimatedGross = netPay / 0.8m;
                        decimal cpfAmt = estimatedGross - netPay;

                        newPayrollLogs.Add(new PayrollLogModel
                        {
                            Id = Guid.NewGuid().ToString(), // Generate UUID for Log
                            EmployeeId = empUuid,           // Link to Employee UUID
                            TransId = t.Id ?? 0,
                            NetPay = netPay,
                            GrossSalary = Math.Round(estimatedGross, 2),
                            CpfAmount = Math.Round(cpfAmt, 2),
                            BaseSalary = Math.Round(estimatedGross, 2),
                            SalaryMonth = t.Date.Month,
                            SalaryYear = t.Date.Year,
                            OtHours = 0,
                            OvertimePay = 0
                        });
                    }
                }
            }

            // 6. UPSERT LOGS
            if (newPayrollLogs.Any())
            {
                var distinctLogs = newPayrollLogs.GroupBy(x => x.TransId).Select(g => g.First()).ToList();
                var options = new Postgrest.QueryOptions { Upsert = true, OnConflict = "trans_id" };

                try
                {
                    await _supabase.From<PayrollLogModel>().Upsert(distinctLogs, options);
                    Debug.WriteLine($"[DB SUCCESS] Saved {distinctLogs.Count} payroll records.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[DB ERROR] Payroll Sync Failed: {ex.Message}");
                }
            }
        }
        private decimal ParseSafeDecimal(IXLCell cell)
        {
            if (cell == null || cell.Value.IsBlank) return 0;
            if (cell.Value.IsNumber) return (decimal)cell.Value.GetNumber();
            string val = cell.GetString().Replace("$", "").Replace(",", "").Trim();
            return decimal.TryParse(val, out decimal res) ? res : 0;
        }
        private async Task<List<AnnualSummaryModel>> ProcessCategoriesAndSummaries(IXLWorksheet sheet, string email, string baseType, List<CategoryModel> cache)
        {
            var rows = sheet.RowsUsed();
            var parentStack = new Dictionary<int, int?>();
            string currentSectionType = baseType;
            var summaries = new List<AnnualSummaryModel>();

            Debug.WriteLine($"[TRACE] Analyzing Structure: {sheet.Name}");

            // Identify Year Columns
            var headerRow = rows.FirstOrDefault(r => r.Cells().Any(c => c.Value.ToString().Contains("2")));
            var yearCols = new Dictionary<int, int>();
            if (headerRow != null)
                foreach (var cell in headerRow.Cells().Where(c => c.Value.ToString().Contains("2")))
                    yearCols[cell.Address.ColumnNumber] = cell.Value.ToString().Contains("22") ? 2022 : 2023;

            foreach (var row in rows)
            {
                var firstCell = row.Cells().FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.GetString()));
                if (firstCell == null) continue;

                int depth = firstCell.Address.ColumnNumber;
                string catName = firstCell.GetString().Trim();
                string lowerName = catName.ToLower();

                // Update section type based on keywords
                if (lowerName.Contains("income") || lowerName.Contains("revenue")) currentSectionType = "Income";
                else if (lowerName.Contains("expense")) currentSectionType = "Expense";
                else if (lowerName.Contains("liabilities")) currentSectionType = "Liability";
                else if (lowerName.Contains("asset")) currentSectionType = "Asset";

                if (lowerName.Contains("total") || lowerName.Contains("account") || lowerName.Contains("net income")) continue;

                // Manage hierarchy stack
                var keysToRemove = parentStack.Keys.Where(k => k >= depth).ToList();
                foreach (var key in keysToRemove) parentStack.Remove(key);
                int? parentId = parentStack.OrderByDescending(k => k.Key).Where(k => k.Key < depth).Select(k => k.Value).FirstOrDefault();

                // Find or Create Category
                var existing = cache.FirstOrDefault(x => x.Name.ToLower().Trim() == lowerName && x.Type == currentSectionType && x.ParentId == parentId);
                int? currentId;

                if (existing != null)
                {
                    currentId = existing.Id;
                }
                else
                {
                    var newCat = new CategoryModel { Name = catName, Type = currentSectionType, ParentId = parentId, IsActive = true };
                    var res = await _supabase.From<CategoryModel>().Insert(newCat);
                    currentId = res.Models.First().Id;
                    cache.Add(res.Models.First());
                    Debug.WriteLine($"[DB] Category Created: {catName} (ID: {currentId})");
                }
                parentStack[depth] = currentId;

                // Collect Summary Data
                if (headerRow != null)
                {
                    foreach (var cell in row.Cells().Where(c => headerRow.Cell(c.Address.ColumnNumber).Value.ToString().Contains("2")))
                    {
                        if (cell.Value.IsNumber)
                        {
                            summaries.Add(new AnnualSummaryModel
                            {
                                UserId = email.ToLower().Trim(),
                                CategoryId = currentId,
                                Year = headerRow.Cell(cell.Address.ColumnNumber).Value.ToString().Contains("22") ? 2022 : 2023,
                                AnnualTotalActual = (decimal)cell.Value.GetNumber(),
                                ReportType = currentSectionType
                            });
                        }
                    }
                }
            }
            return summaries;
        }
        
    }
}
