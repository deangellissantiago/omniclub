using Checkin.Application.DTOs.Checkins;

namespace Checkin.Application.DTOs.Dashboard;

public record AppCountDto(string App, long Total);

public record DashboardSummaryDto(
    IReadOnlyList<AppCountDto> CheckinsByApp,
    IReadOnlyList<AppCountDto> StudentsByApp,
    long TotalCheckins,
    long TotalStudents,
    IReadOnlyList<CheckinDto> RecentCheckins);
