namespace BIP_SMEMC.Models
{
    public class PayrollSummary
    {
        public string MonthYear { get; set; }
        public decimal TotalPayroll { get; set; }
        public int NumberOfEmployees { get; set; }
        public int SelectedMonth { get; set; }
        public int SelectedYear { get; set; }
    }
}
