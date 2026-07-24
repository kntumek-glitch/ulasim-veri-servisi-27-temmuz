namespace ulasım_veri_servisi.Exceptions
{
    public class ServiceUnavailableException : Exception
    {
        public ServiceUnavailableException(string message)
            : base(message)
        {
        }
    }
}