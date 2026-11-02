using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Panoptes_v1.Services.Localization
{
    // Τα validation attributes χωρίς δικό τους μήνυμα παίρνουν το αγγλικό default ως ErrorMessage,
    // ώστε να περάσει από το IStringLocalizer<SharedResource> (DataAnnotations localization).
    public class LocalizedValidationMetadataProvider : IValidationMetadataProvider
    {
        public void CreateValidationMetadata(ValidationMetadataProviderContext context)
        {
            foreach (var attribute in context.ValidationMetadata.ValidatorMetadata.OfType<ValidationAttribute>())
            {
                if (!string.IsNullOrEmpty(attribute.ErrorMessage) || attribute.ErrorMessageResourceName != null)
                    continue;

                attribute.ErrorMessage = attribute switch
                {
                    RequiredAttribute => "The {0} field is required.",
                    StringLengthAttribute { MinimumLength: > 0 } => "The field {0} must be a string with a minimum length of {2} and a maximum length of {1}.",
                    StringLengthAttribute => "The field {0} must be a string with a maximum length of {1}.",
                    MaxLengthAttribute => "The field {0} must be a string or array type with a maximum length of '{1}'.",
                    MinLengthAttribute => "The field {0} must be a string or array type with a minimum length of '{1}'.",
                    EmailAddressAttribute => "The {0} field is not a valid e-mail address.",
                    RangeAttribute => "The field {0} must be between {1} and {2}.",
                    CompareAttribute => "'{0}' and '{1}' do not match.",
                    PhoneAttribute => "The {0} field is not a valid phone number.",
                    _ => attribute.ErrorMessage,
                };
            }
        }
    }

    // Τα μηνύματα του model binding (π.χ. "The value 'x' is not valid.") από το ίδιο αρχείο μεταφράσεων
    public class LocalizedMvcOptions(IStringLocalizerFactory factory) : IConfigureOptions<MvcOptions>
    {
        public void Configure(MvcOptions options)
        {
            var t = factory.Create(typeof(SharedResource));
            options.ModelMetadataDetailsProviders.Add(new LocalizedValidationMetadataProvider());

            var messages = options.ModelBindingMessageProvider;
            messages.SetValueIsInvalidAccessor(value => t["The value '{0}' is invalid.", value]);
            messages.SetValueMustBeANumberAccessor(field => t["The field {0} must be a number.", field]);
            messages.SetValueMustNotBeNullAccessor(value => t["The value '{0}' is invalid.", value]);
            messages.SetAttemptedValueIsInvalidAccessor((value, field) => t["The value '{0}' is not valid for {1}.", value, field]);
            messages.SetMissingKeyOrValueAccessor(() => t["A value is required."]);
            messages.SetMissingBindRequiredValueAccessor(field => t["A value for the '{0}' parameter or property was not provided.", field]);
            messages.SetMissingRequestBodyRequiredValueAccessor(() => t["A non-empty request body is required."]);
            messages.SetNonPropertyAttemptedValueIsInvalidAccessor(value => t["The value '{0}' is not valid.", value]);
            messages.SetNonPropertyUnknownValueIsInvalidAccessor(() => t["The supplied value is invalid."]);
            messages.SetNonPropertyValueMustBeANumberAccessor(() => t["The field must be a number."]);
            messages.SetUnknownValueIsInvalidAccessor(field => t["The supplied value is invalid for {0}.", field]);
        }
    }
}
