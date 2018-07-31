using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace WebHooks.Publisher.Infrastructure
{
    public static class MvcExtensions
    {
        /// <summary>
        /// Registers a singleton only if it has not already been registered. 
        /// </summary>
        /// <typeparam name="T">The type of singleton service to regiser</typeparam>
        /// <param name="services">The collection of services</param>
        /// <param name="builder">The method that builds the services</param>
        /// <param name="lazy">Should the builder be invoked before registering the type?</param>
        /// <returns>The collection for chainability. </returns>
        public static IServiceCollection AddSingletonIfNotExists<T>(this IServiceCollection services, Func<T> builder,
            bool lazy = true) where T : class
        {
            if (services.All(x => x.ServiceType != typeof(T)))
            {
                if (lazy)
                {
                    services.AddSingleton(builder);
                }
                else
                {
                    services.AddSingleton(builder());
                }

            }
            return services;
        }

        /// <summary>
        /// Finds the appropriate controllers
        /// </summary>
        /// <param name="partManager">The manager for the parts</param>
        /// <param name="controllerTypes">The controller types that are allowed. </param>
        public static void UseSpecificControllers(
            this ApplicationPartManager partManager,
            params Type[] controllerTypes)
        {
            partManager.FeatureProviders.Add(new InternalControllerFeatureProvider());
            partManager.ApplicationParts.Clear();
            partManager.ApplicationParts.Add(new SelectedControllersApplicationParts(controllerTypes));
        }

        /// <summary>
        /// Only allow selected controllers
        /// </summary>
        /// <param name="mvcCoreBuilder">The builder that configures mvc core</param>
        /// <param name="controllerTypes">The controller types that are allowed. </param>
        public static IMvcCoreBuilder UseSpecificControllers(
            this IMvcCoreBuilder mvcCoreBuilder,
            params Type[] controllerTypes) => mvcCoreBuilder
                .ConfigureApplicationPartManager(partManager => partManager.UseSpecificControllers(controllerTypes));

        /// <summary>
        /// Only instantiates selected controllers, not all of them. Prevents application scanning for controllers. 
        /// </summary>
        private class SelectedControllersApplicationParts : ApplicationPart, IApplicationPartTypeProvider
        {
            public SelectedControllersApplicationParts()
            {
                Name = "Only allow selected controllers";
            }
            public SelectedControllersApplicationParts(Type[] types)
            {
                Types = types.Select(x => x.GetTypeInfo()).ToArray();
            }

            public override string Name { get; }

            public IEnumerable<TypeInfo> Types { get; }
        }

        /// <summary>
        /// Ensure that internal controllers are also allowed. The default controllerfeatureprovider 
        /// hides internal controllers, but this one allows it. 
        /// </summary>
        private class InternalControllerFeatureProvider : ControllerFeatureProvider
        {
            private const string ControllerTypeNameSuffix = "Controller";

            /// <summary>
            /// Determines if a given <paramref name="typeInfo"/> is a controller. The default controllerfeatureprovider 
            /// hides internal controllers, but this one allows it. 
            /// </summary>
            /// <param name="typeInfo">The <see cref="TypeInfo"/> candidate.</param>
            /// <returns><code>true</code> if the type is a controller; otherwise <code>false</code>.</returns>
            protected override bool IsController(TypeInfo typeInfo)
            {
                if (!typeInfo.IsClass)
                {
                    return false;
                }

                if (typeInfo.IsAbstract)
                {
                    return false;
                }

                if (typeInfo.ContainsGenericParameters)
                {
                    return false;
                }

                if (typeInfo.IsDefined(typeof(Microsoft.AspNetCore.Mvc.NonControllerAttribute)))
                {
                    return false;
                }

                if (!typeInfo.Name.EndsWith(ControllerTypeNameSuffix, StringComparison.OrdinalIgnoreCase) &&
                    !typeInfo.IsDefined(typeof(Microsoft.AspNetCore.Mvc.ControllerAttribute)))
                {
                    return false;
                }

                return true;
            }
        }
    }
}
