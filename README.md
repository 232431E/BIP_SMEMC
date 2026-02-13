# BIP_SMEMC
# OptiFlow.AI - SME Financial & Operational Management System

OptiFlow.AI is a comprehensive web application designed to empower Small and Medium Enterprises (SMEs) in Singapore. It streamlines financial management, automates credit scoring, facilitates access to funding, manages HR compliance (Payroll/CPF), and provides educational resources.

## 📂 Application Page Breakdown & Feature Outline

Below is a detailed outline of the pages created within the application, categorized by their operational domain.

### 1. Dashboard (Command Center)

**Page:** `Dashboard/Index`

* **Purpose:** To provide an immediate, high-level snapshot of the company's financial and operational health.
* **Key Functions & Features:**
* **Financial Widgets:** Displays total revenue, expenses, and net profit in real-time.
* **Credit Score Summary:** Shows the latest calculated credit score and rating.
* **Interactive Charts:** Visual graphs for cash flow trends and expense breakdowns.
* **Quick Actions:** Shortcuts to create invoices, log expenses, or run payroll.
* **AI Insights:** Integration with Gemini AI to provide a daily financial tip based on current data.


* **Value to User:** Gives business owners instant visibility into their performance without digging through spreadsheets, allowing for faster decision-making.

---

### 2. Invoicing System (Revenue Management)

**Pages:** `Invoices/InvoiceView`, `CreateInvoice`, `EditInvoice`, `InvoiceDetails`, `PaymentInsights`

* **Purpose:** To manage the entire lifecycle of accounts receivable, from creation to payment collection.
* **Key Functions & Features:**
* **CRUD Operations:** Create, Read, Update, and Delete invoices.
* **Dynamic Line Items:** Add multiple products/services with automatic total calculation.
* **Status Tracking:** Visual badges for 'Draft', 'Sent', 'Overdue', 'Paid', and 'Partially Paid'.
* **PDF Generation:** Generate professional PDF invoices for download.
* **Payment Recording:** Log partial or full payments against specific invoices.
* **Auto-Overdue Detection:** System automatically flags invoices past their due date.
* **AI Email Reminders:** Generate tone-adjusted email drafts (Friendly vs. Urgent) for overdue clients.


* **Value to User:** Accelerates cash flow by professionalizing the billing process and automating the tracking of outstanding debts.

---

### 3. Financial Management (Cash Flow, Budget, Expenses, Debt)

**Pages:** `CashFlow/Index`, `Budget/Index`, `Expense/Index`, `Debt/Index`

* **Purpose:** To provide granular control over money going in and out of the business.
* **Key Functions & Features:**
* **Expense Logging:** Categorize daily expenses (e.g., Rent, Utilities, Marketing).
* **Budget Setting:** Set monthly limits for specific categories and track variance.
* **Debt Tracking:** Record loans, interest rates, and monthly repayment obligations.
* **Visual Analytics:** Bar and pie charts showing spending distribution.


* **Value to User:** Prevents overspending, highlights cost-saving opportunities, and ensures debt obligations are managed responsibly.

---

### 4. Smart Credit Scoring & Lending (Funding Access)

**Pages:** `CreditScore/Calculator`, `Creditresults`, `Credithistory`, `Lender/Find`

* **Purpose:** To assess the business's creditworthiness and match them with suitable loan providers.
* **Key Functions & Features:**
* **Proprietary Algorithm:** Calculates a score (0-2000) based on Revenue, Profit Margin, Debt Ratio, and Business Maturity.
* **Detailed Reports:** Generates a downloadable PDF Credit Report with a breakdown of factors (Profitability, Debt Mgmt, etc.).
* **Scenario Simulator:** Allows users to adjust inputs (e.g., "What if revenue increases by 20%?") to see how it affects their score.
* **Lender Matching:** Automatically recommends specific lenders (Banks, Fintech, Gov Schemes) based on the calculated score.
* **Application Submission:** Submit interest to lenders directly through the platform.


* **Value to User:** Demystifies the lending process. Users know their eligibility *before* applying, reducing rejection rates and finding the best interest rates.

---

### 5. Verification System (Compliance & Trust)

**Pages:** `VerificationSME/Submit`, `VerificationAdmin/Verificationlist`, `Adminreview`

* **Purpose:** To validate the financial data provided by the SME using official documents.
* **Key Functions & Features:**
* **Document Upload:** Secure upload for bank statements, ACRA profiles, and financial statements.
* **Admin Review Portal:** Dedicated interface for admins to review documents and Approve/Reject requests.
* **Status Workflow:** Tracks verification status (Pending -> Approved/Rejected) with comments.


* **Value to User:** Adds credibility to the user's profile. Verified profiles are more likely to be accepted by lenders within the ecosystem.

---

### 6. Payroll System (HR & Operations)

**Pages:** `Payroll/Index`, `Create`, `Edit`, `Payslip`, `EnterOvertime`

* **Purpose:** To manage employee compensation in compliance with Singapore regulations.
* **Key Functions & Features:**
* **Employee Management:** Store employee details, base salary, and CPF rates.
* **Auto-Calculation:** Automatically calculates Gross Pay, CPF (Employer/Employee), and Net Salary.
* **Overtime Handling:** Logic to calculate 1.5x pay for overtime hours.
* **Payslip Generation:** One-click generation of detailed digital payslips.


* **Value to User:** Saves hours of manual calculation and ensures legal compliance with CPF contribution requirements.

---

### 7. Strategic Tools (Profit Improvement & AI)

**Pages:** `ProfitImprovement/Index`, `Chatbot/Index`

* **Purpose:** To provide forward-looking advice and strategy.
* **Key Functions & Features:**
* **Profit Simulator:** Sliders to adjust volume, price, and costs to visualize impact on the bottom line.
* **AI Chatbot:** An integrated assistant powered by Gemini to answer questions about finance terms, government grants, or app usage.


* **Value to User:** Moves beyond simple record-keeping to help the owner plan for growth and optimize profitability.

---

### 8. Community & Learning (Growth Ecosystem)

**Pages:** `Learning/Index`, `Quiz`, `Community/Index`

* **Purpose:** To upskill business owners and provide peer support.
* **Key Functions & Features:**
* **Learning Modules:** Structured lessons on Financial Literacy, Digital Marketing, and Compliance.
* **Interactive Quizzes:** Tests knowledge retention after modules.
* **Discussion Forum:** A space for users to post threads, reply, and vote on helpful content.
* **Gamification/Rewards:** Users earn points for completing quizzes or financial tasks, which can be redeemed for perks.


* **Value to User:** Bridges the knowledge gap for first-time entrepreneurs and provides a support network of fellow business owners.

---

### 9. Account Management

**Pages:** `Account/Login`, `Signup`, `Profile`, `Settings`

* **Purpose:** Security and personalization.
* **Key Functions & Features:**
* **Authentication:** Secure login/signup.
* **2FA (Two-Factor Authentication):** Enhanced security for financial data.
* **Profile Customization:** Management of business details and user preferences.


* **Value to User:** Ensures sensitive financial data is protected and accessible only to authorized personnel.