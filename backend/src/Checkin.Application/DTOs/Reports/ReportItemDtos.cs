using Checkin.Application.DTOs.Dashboard;

namespace Checkin.Application.DTOs.Reports;

public record StudentReportItemDto(string StudentId, string StudentName, long TotalCheckins, DateTime? LastCheckinAt);

public record SchoolReportItemDto(string CheckinPointId, string CheckinPointName, string App, long TotalCheckins, DateTime? LastCheckinAt);

public record GeneralReportDto(
    long TotalCheckins,
    IReadOnlyList<AppCountDto> CheckinsByApp,
    IReadOnlyList<StudentReportItemDto> TopStudents,
    IReadOnlyList<SchoolReportItemDto> ByCheckinPoint);
