using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BIP_SMEMC.Services
{
    public class RequireAppAuthAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var email = context.HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrWhiteSpace(email))
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
            }
        }
    }

    public class RequireAdminAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var email = context.HttpContext.Session.GetString("UserEmail");
            var role = context.HttpContext.Session.GetString("UserRole");
            if (string.IsNullOrWhiteSpace(email))
            {
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            {
                context.Result = new RedirectToActionResult("Index", "Dashboard", null);
            }
        }
    }
}
