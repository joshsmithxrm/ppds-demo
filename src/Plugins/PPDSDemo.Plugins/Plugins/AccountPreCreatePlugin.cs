using System;
using Microsoft.Xrm.Sdk;

namespace PPDSDemo.Plugins.Plugins
{
    /// <summary>
    /// Validates account data before creation.
    ///
    /// Registration:
    /// - Entity: account
    /// - Message: Create
    /// - Stage: Pre-operation (20)
    /// - Mode: Synchronous
    /// </summary>
    public class AccountPreCreatePlugin : PluginBase
    {
        protected override void ExecutePlugin(LocalPluginContext context)
        {
            // Get the target entity
            var target = context.GetTargetEntity();
            if (target == null)
            {
                context.Trace("No target entity found, exiting");
                return;
            }

            // Validate the entity is an account
            if (target.LogicalName != "account")
            {
                context.Trace($"Unexpected entity type: {target.LogicalName}");
                return;
            }

            context.Trace("Validating account data...");

            // Example validation: Ensure account name is not empty or whitespace
            ValidateAccountName(target, context);

            // Example: Set default values if not provided
            SetDefaultValues(target, context);

            // Example: Business rule - format phone number
            FormatPhoneNumber(target, context);

            context.Trace("Account validation complete");
        }

        private void ValidateAccountName(Entity account, LocalPluginContext context)
        {
            if (!account.Contains("name"))
            {
                context.Trace("Account name not provided, validation skipped");
                return;
            }

            var name = account.GetAttributeValue<string>("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidPluginExecutionException(
                    "Account name cannot be empty or whitespace.");
            }

            // Example: Enforce minimum length
            if (name.Length < 2)
            {
                throw new InvalidPluginExecutionException(
                    "Account name must be at least 2 characters long.");
            }

            context.Trace($"Account name validated: {name}");
        }

        private void SetDefaultValues(Entity account, LocalPluginContext context)
        {
            // Example: Set default description if not provided
            if (!account.Contains("description"))
            {
                account["description"] = "Created via Dynamics 365";
                context.Trace("Set default description");
            }

            // Example: Set creation source (custom field would be ppds_creationsource)
            // account["ppds_creationsource"] = new OptionSetValue(1); // 1 = Manual Entry
        }

        private void FormatPhoneNumber(Entity account, LocalPluginContext context)
        {
            if (!account.Contains("telephone1"))
            {
                return;
            }

            var phone = account.GetAttributeValue<string>("telephone1");
            if (string.IsNullOrWhiteSpace(phone))
            {
                return;
            }

            // Example: Remove non-numeric characters for consistency
            var digitsOnly = new string(Array.FindAll(phone.ToCharArray(), char.IsDigit));

            // Format as (XXX) XXX-XXXX if 10 digits
            if (digitsOnly.Length == 10)
            {
                var formatted = $"({digitsOnly.Substring(0, 3)}) {digitsOnly.Substring(3, 3)}-{digitsOnly.Substring(6, 4)}";
                account["telephone1"] = formatted;
                context.Trace($"Formatted phone number: {formatted}");
            }
        }
    }
}
