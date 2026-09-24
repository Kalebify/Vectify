namespace Vectify.Api.Contracts;

/// <summary>
/// Forma común de error controlado que devuelve la Web API. Code es estable y
/// pensado para que React lo mapee a copy sin parsear Message (que es para
/// logs/debug humano). Valores de Code usados por el endpoint de upload:
/// "empty_file" | "file_too_large" | "unsupported_format" | "corrupt_file" |
/// "upload_interrupted" | "storage_failure" | "not_found" | "internal_error".
/// </summary>
public sealed record ApiErrorResponse(string Code, string Message);
