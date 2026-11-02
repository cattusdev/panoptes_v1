namespace Panoptes_v1.Services
{
    // Ένας επιχειρηματικός κανόνας δεν επιτρέπει την κίνηση (ARCHITECTURE §1.4). Οι services το πετούν και ο
    // GlobalExceptionHandler το κάνει ProblemDetails με σταθερό "code" για το UI και τα tests.
    // Message = το αγγλικό κείμενο (κλειδί του IStringLocalizer), Arguments = οι τιμές του {0}, {1}...
    // Field = το πεδίο της φόρμας (γίνεται και errors.{field}), Details = επιπλέον στοιχεία (π.χ. τα πιθανά διπλότυπα).
    public class BusinessRuleException(string code, string message, params object[] arguments) : Exception(message)
    {
        public string Code { get; } = code;
        public object[] Arguments { get; } = arguments;
        public int StatusCode { get; init; } = StatusCodes.Status409Conflict;
        public string? Field { get; init; }
        public object? Details { get; init; }
    }
}
