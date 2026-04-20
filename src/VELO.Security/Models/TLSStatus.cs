namespace VELO.Security.Models;

public enum TLSStatus
{
    Unknown,
    Valid,
    Expired,
    SelfSigned,
    NoCtLogs,
    Http,           // Plain HTTP, no TLS at all
    Error,
}
