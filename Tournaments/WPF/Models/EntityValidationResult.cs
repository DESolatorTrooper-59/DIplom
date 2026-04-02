namespace Tournaments.WPF.Models
{
    public sealed class EntityValidationResult
    {
        private EntityValidationResult(bool isValid, string message)
        {
            IsValid = isValid;
            Message = message;
        }

        public bool IsValid { get; }

        public string Message { get; }

        public static EntityValidationResult Success()
        {
            return new EntityValidationResult(true, string.Empty);
        }

        public static EntityValidationResult Fail(string message)
        {
            return new EntityValidationResult(false, message);
        }
    }
}
