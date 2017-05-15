namespace WebHooks.Publisher.Api
{
    using System.Net;
    using System.Net.Http;
    using System.Web.Http;
    using System.Web.Http.Filters;

    internal class NullFilter : ActionFilterAttribute
    {
        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            var response = actionExecutedContext.Response;

            var hasContent = response.TryGetContentValue(out object _);

            if (!hasContent)
            {
                throw new HttpResponseException(HttpStatusCode.NotFound);
            }
        }
    }
}