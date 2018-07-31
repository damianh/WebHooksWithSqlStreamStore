using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using WebHooks.Publisher.Api;
using WebHooks.Publisher.Domain;
using WebHooks.Publisher.Infrastructure;

namespace WebHooks.Publisher
{
    public class WebHookPublisherStartup
    {
        private readonly WebHookPublisherSettings _settings;

        public WebHookPublisherStartup(WebHookPublisherSettings settings)
        {
            _settings = settings;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(_settings.StreamStore);
            services.AddSingleton(new WebHooksRepository(
                _settings.StreamStore,
                "webhooks", 
                _settings.GetUtcNow,
                _settings.MaxWebHookCount));

            services
                .AddMvcCore()
                .AddJsonFormatters(serializerSettings =>
                {
                    serializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
#if DEBUG
                    serializerSettings.Formatting =
                        Formatting.Indented; //indentation bloats the message size, only use in debug mode.
#endif
                })
                .UseSpecificControllers(typeof(PublisherController));
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseMvc();
        }
    }
}