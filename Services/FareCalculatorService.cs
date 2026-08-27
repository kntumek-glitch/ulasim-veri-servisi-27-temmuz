using System;
using System.Collections.Generic;
using System.Linq;
using TransportDataService.Models.Gtfs.JourneyPlan;

namespace ulasim_veri_servisi.Services;

public static class FareCalculatorService
{
    // ESHOT 2026 Fares
    private const double TAM_BOARDING_FARE = 35.0;
    private const double TAM_TRANSFER_FARE = 0.0;
    
    private const double OGRENCI_BOARDING_FARE = 17.5;
    private const double OGRENCI_TRANSFER_FARE = 0.0;

    private const int TRANSFER_WINDOW_MINUTES = 90;

    public static List<FareDto> CalculateFares(ItineraryDto itinerary)
    {
        var fares = new List<FareDto>();
        var transitLegs = itinerary.Legs.Where(l => l.Mode == "TRANSIT").ToList();

        if (!transitLegs.Any())
            return fares; // Only walk

        fares.Add(CalculateFareForProfile(transitLegs, "Tam", TAM_BOARDING_FARE, TAM_TRANSFER_FARE));
        fares.Add(CalculateFareForProfile(transitLegs, "Öğrenci", OGRENCI_BOARDING_FARE, OGRENCI_TRANSFER_FARE));

        return fares;
    }

    private static FareDto CalculateFareForProfile(List<LegDto> transitLegs, string profileName, double boardingFare, double transferFare)
    {
        var fareDto = new FareDto
        {
            FareType = profileName,
            Currency = "TRY",
            TotalFare = 0
        };

        DateTimeOffset? firstBoardingTime = null;

        for (int i = 0; i < transitLegs.Count; i++)
        {
            var leg = transitLegs[i];
            var legBoardingTime = leg.DepartureTime;

            if (legBoardingTime == null)
                continue;

            double amountCharged = 0;
            bool isTransfer = false;
            string description;

            if (firstBoardingTime == null)
            {
                // First boarding
                firstBoardingTime = legBoardingTime;
                amountCharged = boardingFare;
                description = "İlk Biniş";
            }
            else
            {
                // Check transfer window
                var timeSinceFirstBoarding = legBoardingTime.Value - firstBoardingTime.Value;
                
                if (timeSinceFirstBoarding.TotalMinutes <= TRANSFER_WINDOW_MINUTES)
                {
                    // Within transfer window
                    amountCharged = transferFare;
                    isTransfer = true;
                    description = $"{i}. Aktarma";
                }
                else
                {
                    // Transfer window expired, start new cycle
                    firstBoardingTime = legBoardingTime;
                    amountCharged = boardingFare;
                    description = "Süre Aşımı (Yeni İlk Biniş)";
                }
            }

            fareDto.TotalFare += amountCharged;
            fareDto.Breakdown.Add(new FareLegDto
            {
                LegId = leg.RouteId ?? leg.RouteShortName ?? "TRANSIT",
                RouteShortName = leg.RouteShortName ?? leg.RouteId ?? "Bilinmiyor",
                Amount = amountCharged,
                IsTransfer = isTransfer,
                Description = description
            });
        }

        return fareDto;
    }
}
