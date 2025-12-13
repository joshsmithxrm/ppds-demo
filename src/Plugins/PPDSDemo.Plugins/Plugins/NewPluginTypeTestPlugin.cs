using System;
using Microsoft.Xrm.Sdk;
using PPDSDemo.Sdk;

namespace PPDSDemo.Plugins.Plugins
{
    /// <summary>
    /// Test plugin WITH a PluginStep attribute.
    /// This plugin type SHOULD be auto-created when deployed to an existing assembly
    /// because it has steps configured.
    /// </summary>
    [PluginStep(
        Message = "Update",
        EntityLogicalName = "account",
        Stage = PluginStage.PostOperation,
        Mode = PluginMode.Asynchronous,
        FilteringAttributes = "description")]
    public class NewPluginTypeTestPlugin : PluginBase
    {
        protected override void ExecutePlugin(LocalPluginContext context)
        {
            context.Trace("NewPluginTypeTestPlugin executed - testing auto plugin type creation");

            var target = context.GetTargetEntity();
            if (target == null)
            {
                context.Trace("No target entity found");
                return;
            }

            context.Trace($"Account description updated");
        }
    }
}
