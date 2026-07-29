using System.ComponentModel.DataAnnotations;

namespace TransportDataService.Models.Gtfs.JourneyPlan;

public class CoordinateDto
{
    [Required]
    [Range(-90.0, 90.0, ErrorMessage = "Enlem (Latitude) -90 ile 90 arasında olmalıdır.")]
    public double Lat { get; set; }

    [Required]
    [Range(-180.0, 180.0, ErrorMessage = "Boylam (Longitude) -180 ile 180 arasında olmalıdır.")]
    public double Lon { get; set; }
}
