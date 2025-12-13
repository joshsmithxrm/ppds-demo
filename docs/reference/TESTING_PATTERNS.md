# Testing Patterns

Patterns for unit testing Dynamics 365 plugins and workflow activities.

> **Note:** Test projects are not yet implemented in this demo solution. These patterns show recommended testing approaches that can be added when needed. The plugin code (`src/Plugins/PPDSDemo.Plugins/`) is ready to be tested using these patterns.

---

## Plugin Unit Testing with FakeXrmEasy

```csharp
[TestClass]
public class AccountPreCreatePluginTests
{
    private XrmFakedContext _context;
    private IOrganizationService _service;

    [TestInitialize]
    public void Setup()
    {
        _context = new XrmFakedContext();
        _service = _context.GetOrganizationService();
    }

    [TestMethod]
    public void Execute_WithValidAccount_ShouldSucceed()
    {
        // Arrange
        var target = new Entity("account")
        {
            Id = Guid.NewGuid(),
            ["name"] = "Test Account",
            ["ppds_accounttype"] = new OptionSetValue(1)
        };

        var pluginContext = _context.GetDefaultPluginContext();
        pluginContext.InputParameters["Target"] = target;
        pluginContext.MessageName = "Create";
        pluginContext.Stage = 20; // Pre-operation

        // Act
        _context.ExecutePluginWith<AccountPreCreatePlugin>(pluginContext);

        // Assert
        // Verify expected behavior
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidPluginExecutionException))]
    public void Execute_WithInvalidAccount_ShouldThrow()
    {
        // Arrange
        var target = new Entity("account")
        {
            Id = Guid.NewGuid()
            // Missing required field
        };

        var pluginContext = _context.GetDefaultPluginContext();
        pluginContext.InputParameters["Target"] = target;
        pluginContext.MessageName = "Create";

        // Act
        _context.ExecutePluginWith<AccountPreCreatePlugin>(pluginContext);

        // Assert - Exception expected
    }
}
```

---

## Test Structure

### Arrange-Act-Assert Pattern

```csharp
[TestMethod]
public void MethodName_Scenario_ExpectedBehavior()
{
    // Arrange - Set up test data and context
    var target = new Entity("account") { Id = Guid.NewGuid() };

    // Act - Execute the code under test
    _context.ExecutePluginWith<MyPlugin>(pluginContext);

    // Assert - Verify the outcome
    Assert.IsTrue(condition);
}
```

### Plugin Context Setup

| Property | Description | Example |
|----------|-------------|---------|
| `InputParameters["Target"]` | The entity being operated on | `new Entity("account")` |
| `MessageName` | The SDK message | `"Create"`, `"Update"`, `"Delete"` |
| `Stage` | Pipeline stage | `10` (Pre-validation), `20` (Pre-operation), `40` (Post-operation) |
| `PreEntityImages` | Entity state before operation | For Update/Delete |
| `PostEntityImages` | Entity state after operation | For Post-operation |

---

## Testing Frameworks

| Framework | Use Case |
|-----------|----------|
| **FakeXrmEasy** | Plugin and workflow activity unit testing |
| **MSTest** | Test runner (built into Visual Studio) |
| **Moq** | Mocking dependencies |

---

## Best Practices

- Test one behavior per test method
- Use descriptive test names: `MethodName_Scenario_ExpectedBehavior`
- Test both success and failure paths
- Mock external dependencies
- Keep tests fast and isolated

---

## See Also

- [PLUGIN_COMPONENTS_REFERENCE.md](PLUGIN_COMPONENTS_REFERENCE.md) - Plugin development patterns
- [FakeXrmEasy Documentation](https://dynamicsvalue.github.io/fake-xrm-easy-docs/)
- [Microsoft: Test plugins](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/unit-testing-plugins)
