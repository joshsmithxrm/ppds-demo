using System;
using Microsoft.Xrm.Sdk;

namespace PPDSDemo.Plugins.Plugins
{
    /// <summary>
    /// Demonstrates post-operation plugin with image handling.
    /// Creates an audit-style note when key contact fields change.
    ///
    /// Registration:
    /// - Entity: contact
    /// - Message: Update
    /// - Stage: Post-operation (40)
    /// - Mode: Asynchronous (recommended for non-critical post operations)
    /// - Pre-Image: PreImage (with emailaddress1, jobtitle, telephone1)
    /// </summary>
    public class ContactPostUpdatePlugin : PluginBase
    {
        // Fields we want to track changes for
        private static readonly string[] TrackedFields = new[]
        {
            "emailaddress1",
            "jobtitle",
            "telephone1"
        };

        protected override void ExecutePlugin(LocalPluginContext context)
        {
            var target = context.GetTargetEntity();
            if (target == null)
            {
                context.Trace("No target entity found, exiting");
                return;
            }

            if (target.LogicalName != "contact")
            {
                context.Trace($"Unexpected entity type: {target.LogicalName}");
                return;
            }

            // Get the pre-image to compare values
            var preImage = context.GetPreImage("PreImage");
            if (preImage == null)
            {
                context.Trace("No pre-image configured, cannot detect changes");
                return;
            }

            context.Trace("Checking for tracked field changes...");

            // Build a list of changes
            var changes = DetectChanges(target, preImage, context);

            if (changes.Length == 0)
            {
                context.Trace("No tracked fields changed");
                return;
            }

            // Create an audit note (example of creating related record)
            CreateAuditNote(context, target.Id, changes);
        }

        private string DetectChanges(Entity target, Entity preImage, LocalPluginContext context)
        {
            var changeLog = new System.Text.StringBuilder();

            foreach (var field in TrackedFields)
            {
                // Only check if the field was included in the update
                if (!target.Contains(field))
                {
                    continue;
                }

                var oldValue = preImage.Contains(field) ? preImage[field]?.ToString() : "(empty)";
                var newValue = target[field]?.ToString() ?? "(empty)";

                // Check if value actually changed
                if (!string.Equals(oldValue, newValue, StringComparison.OrdinalIgnoreCase))
                {
                    context.Trace($"Field '{field}' changed: '{oldValue}' -> '{newValue}'");
                    changeLog.AppendLine($"- {GetFieldDisplayName(field)}: '{oldValue}' → '{newValue}'");
                }
            }

            return changeLog.ToString();
        }

        private string GetFieldDisplayName(string fieldName)
        {
            return fieldName switch
            {
                "emailaddress1" => "Email",
                "jobtitle" => "Job Title",
                "telephone1" => "Business Phone",
                _ => fieldName
            };
        }

        private void CreateAuditNote(LocalPluginContext context, Guid contactId, string changes)
        {
            context.Trace("Creating audit note...");

            var note = new Entity("annotation")
            {
                ["subject"] = "Contact Information Updated",
                ["notetext"] = $"The following fields were updated:\n\n{changes}\n\nUpdated at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                ["objectid"] = new EntityReference("contact", contactId),
                ["objecttypecode"] = "contact"
            };

            try
            {
                var noteId = context.OrganizationService.Create(note);
                context.Trace($"Created audit note: {noteId}");
            }
            catch (Exception ex)
            {
                // Don't fail the main operation if note creation fails
                // In production, you might want to log this differently
                context.Trace($"Warning: Failed to create audit note: {ex.Message}");
            }
        }
    }
}
