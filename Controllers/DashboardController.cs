using BIP_SMEMC.Models;
using Microsoft.AspNetCore.Mvc;

namespace BIP_SMEMC.Controllers
{
    public class DashboardController : Controller
    {
        public IActionResult Index()
        {
            // This is where you would eventually call Gemini API or ML models
            var model = new DashboardViewModel
            {
                UserName = "Eliz",
                TotalRevenue = 54200,
                TotalExpenses = 32100,
                AiSummary = "Your revenue increased by 12% this month. However, supply costs are trending 20% higher than your 30-day average.",
                TaskAction = new NextBestAction
                {
                    Title = "Chase Client X for $500",
                    Priority = "High",
                    Reasoning = "Invoice #104 is 5 days overdue."
                }
            };

            model.Anomalies.Add(new AnomalyAlert
            {
                Metric = "Supplies",
                Variance = 25,
                Tip = "Review vendor log for price hikes."
            });

            return View(model);

        }
    }
}
