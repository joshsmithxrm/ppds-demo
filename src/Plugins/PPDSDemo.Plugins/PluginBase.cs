using System;
using Microsoft.Xrm.Sdk;

namespace PPDSDemo.Plugins
{
    /// <summary>
    /// Base class for all plugins providing common functionality and patterns.
    /// Inherit from this class to get standardized error handling, tracing, and service access.
    /// </summary>
    public abstract class PluginBase : IPlugin
    {
        /// <summary>
        /// Gets the name of the plugin class for tracing purposes.
        /// </summary>
        protected string PluginName => GetType().Name;

        /// <summary>
        /// Main entry point for plugin execution. Sets up context and delegates to ExecutePlugin.
        /// </summary>
        /// <param name="serviceProvider">The service provider from the plugin pipeline.</param>
        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                throw new InvalidPluginExecutionException("serviceProvider is null");
            }

            // Get execution context
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            if (context == null)
            {
                throw new InvalidPluginExecutionException("Failed to get IPluginExecutionContext");
            }

            // Get tracing service - ALWAYS use this for debugging
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            if (tracingService == null)
            {
                throw new InvalidPluginExecutionException("Failed to get ITracingService");
            }

            // Get organization service factory
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            if (serviceFactory == null)
            {
                throw new InvalidPluginExecutionException("Failed to get IOrganizationServiceFactory");
            }

            // Create organization service (as the user who triggered the plugin)
            var service = serviceFactory.CreateOrganizationService(context.UserId);

            // Create local context wrapper
            var localContext = new LocalPluginContext(context, tracingService, service, serviceFactory);

            try
            {
                tracingService.Trace($"{PluginName}: Starting execution");
                tracingService.Trace($"  Message: {context.MessageName}");
                tracingService.Trace($"  Stage: {context.Stage}");
                tracingService.Trace($"  Entity: {context.PrimaryEntityName}");
                tracingService.Trace($"  Depth: {context.Depth}");

                // Call the derived plugin's implementation
                ExecutePlugin(localContext);

                tracingService.Trace($"{PluginName}: Completed successfully");
            }
            catch (InvalidPluginExecutionException)
            {
                // Re-throw InvalidPluginExecutionException as-is (already formatted for user)
                throw;
            }
            catch (Exception ex)
            {
                tracingService.Trace($"{PluginName}: Error - {ex.Message}");
                tracingService.Trace($"Stack trace: {ex.StackTrace}");

                throw new InvalidPluginExecutionException(
                    $"An error occurred in {PluginName}. Please contact your administrator.",
                    ex);
            }
        }

        /// <summary>
        /// Override this method to implement your plugin logic.
        /// </summary>
        /// <param name="context">The local plugin context with all services.</param>
        protected abstract void ExecutePlugin(LocalPluginContext context);
    }

    /// <summary>
    /// Wrapper class containing all the services and context needed for plugin execution.
    /// </summary>
    public class LocalPluginContext
    {
        /// <summary>
        /// Gets the plugin execution context.
        /// </summary>
        public IPluginExecutionContext PluginExecutionContext { get; }

        /// <summary>
        /// Gets the tracing service for debug output.
        /// </summary>
        public ITracingService TracingService { get; }

        /// <summary>
        /// Gets the organization service (as the triggering user).
        /// </summary>
        public IOrganizationService OrganizationService { get; }

        /// <summary>
        /// Gets the organization service factory for creating additional service instances.
        /// </summary>
        public IOrganizationServiceFactory ServiceFactory { get; }

        /// <summary>
        /// Initializes a new instance of the LocalPluginContext class.
        /// </summary>
        public LocalPluginContext(
            IPluginExecutionContext pluginExecutionContext,
            ITracingService tracingService,
            IOrganizationService organizationService,
            IOrganizationServiceFactory serviceFactory)
        {
            PluginExecutionContext = pluginExecutionContext;
            TracingService = tracingService;
            OrganizationService = organizationService;
            ServiceFactory = serviceFactory;
        }

        /// <summary>
        /// Writes a trace message.
        /// </summary>
        /// <param name="message">The message to trace.</param>
        public void Trace(string message)
        {
            TracingService.Trace(message);
        }

        /// <summary>
        /// Gets the target entity from InputParameters if available.
        /// </summary>
        /// <returns>The target entity or null if not present.</returns>
        public Entity? GetTargetEntity()
        {
            if (PluginExecutionContext.InputParameters.Contains("Target") &&
                PluginExecutionContext.InputParameters["Target"] is Entity entity)
            {
                return entity;
            }
            return null;
        }

        /// <summary>
        /// Gets the target entity reference from InputParameters if available.
        /// </summary>
        /// <returns>The target entity reference or null if not present.</returns>
        public EntityReference? GetTargetEntityReference()
        {
            if (PluginExecutionContext.InputParameters.Contains("Target") &&
                PluginExecutionContext.InputParameters["Target"] is EntityReference entityRef)
            {
                return entityRef;
            }
            return null;
        }

        /// <summary>
        /// Gets a pre-image entity by name.
        /// </summary>
        /// <param name="imageName">The name of the pre-image.</param>
        /// <returns>The pre-image entity or null if not found.</returns>
        public Entity? GetPreImage(string imageName = "PreImage")
        {
            if (PluginExecutionContext.PreEntityImages.Contains(imageName))
            {
                return PluginExecutionContext.PreEntityImages[imageName];
            }
            return null;
        }

        /// <summary>
        /// Gets a post-image entity by name.
        /// </summary>
        /// <param name="imageName">The name of the post-image.</param>
        /// <returns>The post-image entity or null if not found.</returns>
        public Entity? GetPostImage(string imageName = "PostImage")
        {
            if (PluginExecutionContext.PostEntityImages.Contains(imageName))
            {
                return PluginExecutionContext.PostEntityImages[imageName];
            }
            return null;
        }

        /// <summary>
        /// Creates an organization service running as SYSTEM user.
        /// </summary>
        /// <returns>Organization service with SYSTEM context.</returns>
        public IOrganizationService GetSystemService()
        {
            return ServiceFactory.CreateOrganizationService(null);
        }
    }
}
