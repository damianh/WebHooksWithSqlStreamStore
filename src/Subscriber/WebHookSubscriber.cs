namespace WebHooks.Subscriber
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web.Http;
    using System.Web.Http.Dispatcher;
    using LightInject;
    using Microsoft.Owin.Builder;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Serialization;
    using Owin;
    using WebHooks.Subscriber.Api;
    using WebHooks.Subscriber.Domain;

    public class WebHookSubscriber : IDisposable
    {
        private readonly WebHookSubscriberSettings _settings;

        public static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
#if DEBUG
            Formatting = Formatting.Indented //indentation bloats the message size, only use in debug mode.
#endif
        };
        private readonly CancellationTokenSource _disposed = new CancellationTokenSource();
        private readonly SubscriptionsRepository _subscriptionsRepository;

        public WebHookSubscriber(WebHookSubscriberSettings settings)
        {
            _settings = settings;

            _subscriptionsRepository = new SubscriptionsRepository(settings.StreamStore, "webhooks", 
                settings.GetUtcNow, settings.MaxSubscriptionCount);

            var config = CreateHttpConfiguration();
            var appBuilder = new AppBuilder();
            appBuilder.UseWebApi(config);
            AppFunc = appBuilder.Build();
        }

        public Func<IDictionary<string, object>, Task> AppFunc { get; }

        /// <summary>
        ///     Set to true for the webhook receiver endpoint to return an error. Used for testing.
        /// </summary>
        public bool ReturnErrorOnReceive { get; set; }

        private HttpConfiguration CreateHttpConfiguration()
        {
            var config = new HttpConfiguration();
            var container = new ServiceContainer();
            container.RegisterInstance(_settings.StreamStore);
            container.RegisterInstance(_subscriptionsRepository);
            container.RegisterInstance(new WebHookHeaders(_settings.Vendor));
            container.RegisterInstance(new ShouldReturnErrorOnReceive(() => ReturnErrorOnReceive));
            container.EnableWebApi(config);
            var controllerTypeResolver = new ControllerTypeResolver();
            config.Services.Replace(typeof(IHttpControllerTypeResolver), controllerTypeResolver);
            foreach (var controllerType in controllerTypeResolver.GetControllerTypes(null))
            {
                container.Register(controllerType, new PerRequestLifeTime());
            }
            config.MapHttpAttributeRoutes();
            config.Formatters.JsonFormatter.SerializerSettings = SerializerSettings;
            return config;
        }

        private class ControllerTypeResolver : IHttpControllerTypeResolver
        {
            // We want to be very explicit which controllers we want to use.
            // Also we want our controllers internal.

            public ICollection<Type> GetControllerTypes(IAssembliesResolver _)
            {
                return new[] { typeof(SubscriptionController) };
            }
        }

        public void Dispose()
        {
            _disposed.Dispose();
        }
    }
}