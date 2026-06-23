using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ProximaLMS.Models;
using System.Diagnostics;

namespace ProximaLMS.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        // ✅ Global unhandled exceptions (500 errors)
        [Route("Home/Error")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();

            // Log it
            _logger.LogError(feature?.Error, "Unhandled exception on path: {Path}", feature?.Path);

            var model = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                ErrorMessage = feature?.Error.Message ?? "An unexpected error occurred.",
                Path = feature?.Path ?? "Unknown Path"
            };

            Response.StatusCode = 500; // internal server error
            return View(model);
        }

        // ✅ Handles 404, 403, etc.
        [Route("Home/StatusCode")]
        public IActionResult StatusCodeHandler(int code)
        {
            string message = code switch
            {
                404 => "Oops! The page you’re looking for doesn’t exist.",
                403 => "You don’t have permission to access this resource.",
                500 => "Internal Server Error. Please try again later.",
                _ => "An unexpected error occurred."
            };

            _logger.LogWarning("HTTP {Code} error on path {Path}", code, HttpContext.Request.Path);

            var model = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                ErrorMessage = message,
                Path = HttpContext.Request.Path
            };

            Response.StatusCode = code;
            return View("Error", model);
        }

        [Route("Home/AccessDenied")]
        public IActionResult AccessDenied()
        {
            var model = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier,
                ErrorMessage = "Access denied. You do not have permission to view this page."
            };

            Response.StatusCode = 403;
            return View("Error", model);
        }
    }
}
