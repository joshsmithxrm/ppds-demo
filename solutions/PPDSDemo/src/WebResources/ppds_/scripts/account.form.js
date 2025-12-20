/// <reference path="../typings/xrm.d.ts" />

/**
 * My comment from VS Code
 * My comment from the browser
 * My third comment from VS Code
 * Account Form Script
 * Web Resource: ppds_/scripts/account.form.js
 *
 * Registration:
 *   - Form: Account (Main Form)
 *   - Event: OnLoad -> PPDSDemo.Account.onFormLoad
 *   - Event: name OnChange -> PPDSDemo.Account.onNameChange
 */
var PPDSDemo = window.PPDSDemo || {};
PPDSDemo.Account = PPDSDemo.Account || {};

(function () {
    "use strict";

    /**
     * Called when the account form loads.
     * @param {Xrm.Events.EventContext} executionContext - The execution context from the form event.
     */
    this.onFormLoad = function (executionContext) {
        var formContext = executionContext.getFormContext();

        // Show notification banner
        formContext.ui.setFormNotification(
            "Web Resource loaded successfully!",
            "INFO",
            "ppds-load-notification"
        );

        // Clear it after 5 seconds
        setTimeout(function () {
            formContext.ui.clearFormNotification("ppds-load-notification");
        }, 5000);

        // Log account info to console for debugging
        var accountName = formContext.getAttribute("name")?.getValue();
        var accountId = formContext.data.entity.getId();
        console.log("PPDSDemo.Account.onFormLoad:", {
            name: accountName,
            id: accountId,
            formType: formContext.ui.getFormType()
        });
    };

    /**
     * Called when the account name field changes.
     * @param {Xrm.Events.EventContext} executionContext - The execution context from the field event.
     */
    this.onNameChange = function (executionContext) {
        var formContext = executionContext.getFormContext();
        var name = formContext.getAttribute("name")?.getValue();

        if (name && name.toLowerCase().includes("test")) {
            formContext.ui.setFormNotification(
                "Heads up: This looks like a test account",
                "WARNING",
                "ppds-test-account-warning"
            );
        } else {
            formContext.ui.clearFormNotification("ppds-test-account-warning");
        }
    };

}).call(PPDSDemo.Account);
