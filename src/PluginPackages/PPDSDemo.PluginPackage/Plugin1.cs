using Microsoft.Xrm.Sdk;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace PPDSDemo.PluginPackage
{
    /// <summary>
    /// Example plugin demonstrating NuGet dependency usage in a Plugin Package.
    /// This plugin uses Newtonsoft.Json - something not possible in classic plugin assemblies.
    ///
    /// Plugin Packages allow you to:
    /// - Include NuGet dependencies that get deployed with your plugin
    /// - Use libraries like Newtonsoft.Json, Azure SDK, etc.
    /// - Package multiple plugins in a single deployable unit
    ///
    /// Registration:
    /// - Entity: account
    /// - Message: Update
    /// - Stage: Post-operation (40)
    /// - Mode: Asynchronous
    /// </summary>
    public class AccountAuditLogPlugin : PluginBase
    {
        public AccountAuditLogPlugin(string unsecureConfiguration, string secureConfiguration)
            : base(typeof(AccountAuditLogPlugin))
        {
            // Configuration can be passed from plugin registration
            // Useful for environment-specific settings
        }

        protected override void ExecuteDataversePlugin(ILocalPluginContext localPluginContext)
        {
            if (localPluginContext == null)
            {
                throw new ArgumentNullException(nameof(localPluginContext));
            }

            var context = localPluginContext.PluginExecutionContext;
            var tracingService = localPluginContext.TracingService;

            tracingService.Trace("AccountAuditLogPlugin: Starting execution");

            // Get the target entity
            if (!context.InputParameters.Contains("Target") ||
                !(context.InputParameters["Target"] is Entity target))
            {
                tracingService.Trace("No target entity found");
                return;
            }

            if (target.LogicalName != "account")
            {
                tracingService.Trace($"Unexpected entity: {target.LogicalName}");
                return;
            }

            // Create an audit log entry using JSON serialization
            var auditEntry = CreateAuditEntry(context, target, tracingService);

            // Serialize to JSON using Newtonsoft.Json
            // This demonstrates using a NuGet dependency!
            var jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatString = "yyyy-MM-ddTHH:mm:ssZ"
            };

            var auditJson = JsonConvert.SerializeObject(auditEntry, jsonSettings);

            tracingService.Trace($"Audit entry created:\n{auditJson}");

            // In a real scenario, you might:
            // - Store this in a custom audit table
            // - Send to an external logging service
            // - Write to Azure Application Insights
            // - etc.

            tracingService.Trace("AccountAuditLogPlugin: Completed successfully");
        }

        private AuditLogEntry CreateAuditEntry(
            IPluginExecutionContext context,
            Entity target,
            ITracingService tracingService)
        {
            var entry = new AuditLogEntry
            {
                Timestamp = DateTime.UtcNow,
                EntityType = target.LogicalName,
                RecordId = target.Id,
                Operation = context.MessageName,
                UserId = context.UserId,
                Depth = context.Depth,
                Changes = new Dictionary<string, FieldChange>()
            };

            // Log all changed attributes
            foreach (var attribute in target.Attributes)
            {
                var fieldChange = new FieldChange
                {
                    FieldName = attribute.Key,
                    NewValue = FormatAttributeValue(attribute.Value)
                };

                // If we have a pre-image, include the old value
                if (context.PreEntityImages.Contains("PreImage"))
                {
                    var preImage = context.PreEntityImages["PreImage"];
                    if (preImage.Contains(attribute.Key))
                    {
                        fieldChange.OldValue = FormatAttributeValue(preImage[attribute.Key]);
                    }
                }

                entry.Changes[attribute.Key] = fieldChange;
                tracingService.Trace($"Field changed: {attribute.Key}");
            }

            return entry;
        }

        private object? FormatAttributeValue(object? value)
        {
            return value switch
            {
                null => null,
                EntityReference er => new { er.LogicalName, er.Id, er.Name },
                OptionSetValue osv => osv.Value,
                Money m => m.Value,
                DateTime dt => dt.ToString("O"),
                _ => value.ToString()
            };
        }
    }

    /// <summary>
    /// Represents an audit log entry that will be serialized to JSON.
    /// </summary>
    public class AuditLogEntry
    {
        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("entityType")]
        public string EntityType { get; set; } = string.Empty;

        [JsonProperty("recordId")]
        public Guid RecordId { get; set; }

        [JsonProperty("operation")]
        public string Operation { get; set; } = string.Empty;

        [JsonProperty("userId")]
        public Guid UserId { get; set; }

        [JsonProperty("depth")]
        public int Depth { get; set; }

        [JsonProperty("changes")]
        public Dictionary<string, FieldChange> Changes { get; set; } = new Dictionary<string, FieldChange>();
    }

    /// <summary>
    /// Represents a field change in the audit log.
    /// </summary>
    public class FieldChange
    {
        [JsonProperty("fieldName")]
        public string FieldName { get; set; } = string.Empty;

        [JsonProperty("oldValue")]
        public object? OldValue { get; set; }

        [JsonProperty("newValue")]
        public object? NewValue { get; set; }
    }
}
