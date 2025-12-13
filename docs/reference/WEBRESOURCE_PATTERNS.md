# Web Resource Patterns

Patterns and best practices for JavaScript/TypeScript web resources in Dynamics 365 / Dataverse.

---

## Form Script Structure

```typescript
/// <reference path="../typings/xrm.d.ts" />

namespace PPDSDemo.Account {

    // Form context - set on load, used throughout
    let _formContext: Xrm.FormContext;

    /**
     * Called when the account form loads.
     * @param executionContext The execution context from the form event.
     */
    export function onFormLoad(executionContext: Xrm.Events.EventContext): void {
        _formContext = executionContext.getFormContext();

        // Initialize form
        setFieldVisibility();
        registerChangeHandlers();
    }

    /**
     * Called when the account form is saved.
     * @param executionContext The execution context from the form event.
     */
    export function onFormSave(executionContext: Xrm.Events.SaveEventContext): void {
        const saveContext = executionContext.getFormContext();

        // Validation before save
        if (!validateRequiredFields(saveContext)) {
            executionContext.getEventArgs().preventDefault();
        }
    }

    /**
     * Called when the account type field changes.
     */
    export function onAccountTypeChange(): void {
        const accountType = _formContext.getAttribute("ppds_accounttype")?.getValue();

        // React to change
        updateDependentFields(accountType);
    }

    // Private helper functions
    function setFieldVisibility(): void {
        // Implementation
    }

    function registerChangeHandlers(): void {
        _formContext.getAttribute("ppds_accounttype")
            ?.addOnChange(onAccountTypeChange);
    }

    function validateRequiredFields(formContext: Xrm.FormContext): boolean {
        // Validation logic
        return true;
    }

    function updateDependentFields(accountType: number | null): void {
        // Update logic
    }
}
```

---

## Web API Calls

```typescript
namespace PPDSDemo.Api {

    /**
     * Retrieves an account by ID.
     */
    export async function getAccount(accountId: string): Promise<any> {
        try {
            const result = await Xrm.WebApi.retrieveRecord(
                "account",
                accountId,
                "?$select=name,accountnumber,ppds_customfield"
            );
            return result;
        } catch (error) {
            console.error("Error retrieving account:", error);
            throw error;
        }
    }

    /**
     * Calls a custom API.
     */
    export async function callCustomApi(
        apiName: string,
        parameters: Record<string, any>
    ): Promise<any> {
        try {
            const result = await Xrm.WebApi.online.execute({
                getMetadata: () => ({
                    boundParameter: null,
                    operationType: 0, // Action
                    operationName: apiName,
                    parameterTypes: {}
                }),
                ...parameters
            });

            if (result.ok) {
                return await result.json();
            } else {
                throw new Error(`API call failed: ${result.statusText}`);
            }
        } catch (error) {
            console.error(`Error calling ${apiName}:`, error);
            throw error;
        }
    }
}
```

---

## Best Practices

### ALWAYS

- Use namespace pattern to avoid global pollution
- Store `formContext` from `executionContext.getFormContext()`
- Use `?.` optional chaining for attribute access
- Handle async errors with try/catch
- Use TypeScript with Xrm typings

### NEVER

- Use deprecated `Xrm.Page` (use `formContext` instead)
- Use `alert()` for user messages (use `Xrm.Navigation.openAlertDialog`)
- Reference DOM elements directly (forms don't guarantee DOM structure)
- Use `eval()` or dynamic script loading

---

## Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Namespaces | `PPDSDemo.{Area}` | `PPDSDemo.Account` |
| Functions | `camelCase` | `onFormLoad` |
| Event Handlers | `on{Event}` | `onSave`, `onChange` |
| Files | `{entity}.{purpose}.js` | `account.ribbon.js` |

---

## See Also

- [PLUGIN_COMPONENTS_REFERENCE.md](PLUGIN_COMPONENTS_REFERENCE.md) - Plugin patterns
- [Microsoft: Web Resources](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/web-resources)
- [Microsoft: Client API Reference](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference)
