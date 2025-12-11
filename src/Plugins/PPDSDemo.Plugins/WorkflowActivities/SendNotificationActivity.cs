using System;
using System.Activities;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Workflow;

namespace PPDSDemo.Plugins.WorkflowActivities
{
    /// <summary>
    /// Custom workflow activity that demonstrates input/output arguments.
    /// Simulates sending a notification and returns the notification ID.
    ///
    /// This is an example of a CodeActivity for use in classic workflows.
    /// In production, this might integrate with an email service, Teams, etc.
    /// </summary>
    public class SendNotificationActivity : CodeActivity
    {
        #region Input Arguments

        /// <summary>
        /// The recipient's email address.
        /// </summary>
        [Input("Recipient Email")]
        [RequiredArgument]
        public InArgument<string> RecipientEmail { get; set; } = null!;

        /// <summary>
        /// The notification subject/title.
        /// </summary>
        [Input("Subject")]
        [RequiredArgument]
        public InArgument<string> Subject { get; set; } = null!;

        /// <summary>
        /// The notification message body.
        /// </summary>
        [Input("Message")]
        [RequiredArgument]
        public InArgument<string> Message { get; set; } = null!;

        /// <summary>
        /// Optional: Priority level (1=Low, 2=Normal, 3=High).
        /// </summary>
        [Input("Priority (1=Low, 2=Normal, 3=High)")]
        [Default("2")]
        public InArgument<int> Priority { get; set; } = null!;

        #endregion

        #region Output Arguments

        /// <summary>
        /// Returns true if the notification was sent successfully.
        /// </summary>
        [Output("Success")]
        public OutArgument<bool> Success { get; set; } = null!;

        /// <summary>
        /// The unique notification ID (for tracking purposes).
        /// </summary>
        [Output("Notification ID")]
        public OutArgument<string> NotificationId { get; set; } = null!;

        /// <summary>
        /// Error message if the notification failed.
        /// </summary>
        [Output("Error Message")]
        public OutArgument<string> ErrorMessage { get; set; } = null!;

        #endregion

        /// <summary>
        /// Main execution method for the workflow activity.
        /// </summary>
        protected override void Execute(CodeActivityContext context)
        {
            // Get the workflow context and tracing service
            var workflowContext = context.GetExtension<IWorkflowContext>();
            var tracingService = context.GetExtension<ITracingService>();

            tracingService.Trace("SendNotificationActivity: Starting execution");

            try
            {
                // Get input values
                var recipientEmail = RecipientEmail.Get(context);
                var subject = Subject.Get(context);
                var message = Message.Get(context);
                var priority = Priority.Get(context);

                tracingService.Trace($"Recipient: {recipientEmail}");
                tracingService.Trace($"Subject: {subject}");
                tracingService.Trace($"Priority: {priority}");

                // Validate inputs
                if (string.IsNullOrWhiteSpace(recipientEmail))
                {
                    throw new InvalidPluginExecutionException("Recipient email is required.");
                }

                if (!IsValidEmail(recipientEmail))
                {
                    throw new InvalidPluginExecutionException($"Invalid email address: {recipientEmail}");
                }

                if (string.IsNullOrWhiteSpace(subject))
                {
                    throw new InvalidPluginExecutionException("Subject is required.");
                }

                // Simulate sending notification
                // In production, this would call an external service
                var notificationId = SimulateSendNotification(
                    recipientEmail, subject, message, priority, tracingService);

                // Set output values
                Success.Set(context, true);
                NotificationId.Set(context, notificationId);
                ErrorMessage.Set(context, string.Empty);

                tracingService.Trace($"SendNotificationActivity: Completed successfully. ID: {notificationId}");
            }
            catch (Exception ex)
            {
                tracingService.Trace($"SendNotificationActivity: Error - {ex.Message}");

                // Set failure outputs
                Success.Set(context, false);
                NotificationId.Set(context, string.Empty);
                ErrorMessage.Set(context, ex.Message);

                // Re-throw to fail the workflow (optional - could also just return with Success=false)
                throw;
            }
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private string SimulateSendNotification(
            string recipient,
            string subject,
            string message,
            int priority,
            ITracingService tracingService)
        {
            // Generate a unique notification ID
            var notificationId = $"NOTIF-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";

            tracingService.Trace($"Simulating notification send...");
            tracingService.Trace($"  To: {recipient}");
            tracingService.Trace($"  Subject: {subject}");
            tracingService.Trace($"  Priority: {GetPriorityName(priority)}");
            tracingService.Trace($"  Message length: {message?.Length ?? 0} characters");

            // In production, you would:
            // - Call an email service API
            // - Create a queue item
            // - Integrate with Microsoft Graph
            // - etc.

            return notificationId;
        }

        private string GetPriorityName(int priority)
        {
            return priority switch
            {
                1 => "Low",
                2 => "Normal",
                3 => "High",
                _ => "Unknown"
            };
        }
    }
}
