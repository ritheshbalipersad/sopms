using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.IO;
using System.Threading.Tasks;

namespace SOPMSApp.Extensions
{
    public static class ControllerExtensions
    {
        public static async Task<string> RenderViewToStringAsync(this Controller controller, string viewName, object model)
        {
            var serviceProvider = controller.HttpContext.RequestServices;

            var actionContext = new ActionContext(
                controller.HttpContext,
                controller.RouteData,
                new ActionDescriptor()
            );

            var viewEngine = (ICompositeViewEngine)serviceProvider.GetService(typeof(ICompositeViewEngine));
            var tempDataProvider = (ITempDataProvider)serviceProvider.GetService(typeof(ITempDataProvider));

            var viewResult = viewEngine.FindView(actionContext, viewName, false);
            if (!viewResult.Success)
            {
                throw new FileNotFoundException($"View '{viewName}' not found.");
            }

            var viewDictionary = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model
            };

            await using var sw = new StringWriter();
            var viewContext = new ViewContext(
                actionContext,
                viewResult.View,
                viewDictionary,
                new TempDataDictionary(controller.HttpContext, tempDataProvider),
                sw,
                new HtmlHelperOptions()
            );

            await viewResult.View.RenderAsync(viewContext);
            return sw.ToString();
        }
    }
}
