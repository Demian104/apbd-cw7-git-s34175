using ClinicAdoNet.DTOs;
using ClinicAdoNet.Services;
using Microsoft.AspNetCore.Mvc;

namespace ClinicAdoNet.Controllers;

[ApiController]
[Route("api/appointments")]
public class AppointmentsController : ControllerBase
{
    private readonly AppointmentService _service;

    public AppointmentsController(AppointmentService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> GetAppointments(
        [FromQuery] string? status,
        [FromQuery] string? patientLastName)
    {
        var list = await _service.GetAppointmentsAsync(status, patientLastName);
        return Ok(list);
    }

    [HttpGet("{idAppointment:int}")]
    public async Task<IActionResult> GetAppointment(int idAppointment)
    {
        var dto = await _service.GetAppointmentByIdAsync(idAppointment);
        if (dto is null)
            return NotFound(new ErrorResponseDto($"Appointment {idAppointment} not found."));
        return Ok(dto);
    }

    [HttpPost]
    public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto dto)
    {
        var (newId, error, statusCode) = await _service.CreateAppointmentAsync(dto);
        if (error is not null)
            return statusCode == 409
                ? Conflict(new ErrorResponseDto(error))
                : BadRequest(new ErrorResponseDto(error));
        return CreatedAtAction(nameof(GetAppointment),
            new { idAppointment = newId }, new { IdAppointment = newId });
    }

    [HttpPut("{idAppointment:int}")]
    public async Task<IActionResult> UpdateAppointment(
        int idAppointment, [FromBody] UpdateAppointmentRequestDto dto)
    {
        var (found, error, statusCode) = await _service.UpdateAppointmentAsync(idAppointment, dto);
        if (!found) return NotFound(new ErrorResponseDto($"Appointment {idAppointment} not found."));
        if (error is not null)
            return statusCode == 409
                ? Conflict(new ErrorResponseDto(error))
                : BadRequest(new ErrorResponseDto(error));
        return Ok();
    }

    [HttpDelete("{idAppointment:int}")]
    public async Task<IActionResult> DeleteAppointment(int idAppointment)
    {
        var (found, error, _) = await _service.DeleteAppointmentAsync(idAppointment);
        if (!found)  return NotFound(new ErrorResponseDto($"Appointment {idAppointment} not found."));
        if (error is not null) return Conflict(new ErrorResponseDto(error));
        return NoContent();
    }
}