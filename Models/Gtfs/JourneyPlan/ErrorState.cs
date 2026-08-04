namespace ulasim_veri_servisi.Models.Gtfs.JourneyPlan;

public class ErrorState
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }

    public static ErrorState Success() => new ErrorState { IsSuccess = true };
    public static ErrorState Failure(string message, string? code = null) => new ErrorState { IsSuccess = false, ErrorMessage = message, ErrorCode = code };
}
