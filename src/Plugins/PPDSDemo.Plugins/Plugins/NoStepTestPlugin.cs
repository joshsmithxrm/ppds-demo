using System;
using Microsoft.Xrm.Sdk;

namespace PPDSDemo.Plugins.Plugins
{
    /// <summary>
    /// Test plugin WITHOUT a PluginStep attribute.
    /// This plugin type should NOT be created in Dataverse because it has no steps configured.
    /// Used to verify that the deployment tooling only creates plugin types when steps exist.
    /// </summary>
    public class NoStepTestPlugin : PluginBase
    {
        protected override void ExecutePlugin(LocalPluginContext context)
        {
            context.Trace("NoStepTestPlugin executed");
        }
    }
}
