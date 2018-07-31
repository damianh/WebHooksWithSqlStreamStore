using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using WebHooks.Subscriber.Api;
using WebHooks.Subscriber.Domain;
using WebHooks.Subscriber.Infrastructure;

namespace WebHooks.Subscriber
{
    public class WebHookSubscriberStartup
    {
        private readonly WebHookSubscriberSettings _settings;

        public WebHookSubscriberStartup(WebHookSubscriberSettings settings)
        {
            _settings = settings;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(_settings.StreamStore);
            services.AddSingleton(s => new SubscriptionsRepository(
                _settings.StreamStore,
                s.GetService<JsonSerializerSettings>(),
                "webhooks",
                _settings.GetUtcNow,
                _settings.MaxSubscriptionCount));
            services.AddSingleton(new ShouldReturnErrorOnReceive(() => _settings.ReturnErrorOnReceive));
            services.AddSingleton(new WebHookHeaders(_settings.Vendor));

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
                .UseSpecificControllers(typeof(SubscriptionController));
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseMvc();
        }
    }
}