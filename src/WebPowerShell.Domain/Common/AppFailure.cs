namespace WebPowerShell.Domain.Common
{
    public record AppFailure(string ErrorCode, string Message)
    {
        public static readonly AppFailure Unauthorized = new("Unauthorized", "인증되지 않은 사용자입니다.");
        public static readonly AppFailure Forbidden = new("Forbidden", "기능 접근 권한이 없습니다.");
        public static readonly AppFailure PasswordExpired = new("PasswordExpired", "비밀번호 변경이 필요합니다.");
        public static readonly AppFailure SessionNotFound = new("SessionNotFound", "존재하지 않는 세션입니다.");
        public static readonly AppFailure SessionExpired = new("SessionExpired", "유휴 시간 초과로 종료된 세션입니다.");
        public static readonly AppFailure SessionBusy = new("SessionBusy", "세션에서 다른 명령이 실행 중입니다.");
        public static readonly AppFailure TabLimitExceeded = new("TabLimitExceeded", "최대 탭 수 제한을 초과했습니다.");
        public static readonly AppFailure CommandTooLong = new("CommandTooLong", "명령어 길이 제한을 초과했습니다.");
        public static readonly AppFailure CommandTimedOut = new("CommandTimedOut", "명령 실행 시간이 초과되었습니다.");
        public static readonly AppFailure RateLimitExceeded = new("RateLimitExceeded", "요청 횟수 제한을 초과했습니다.");
        public static readonly AppFailure InternalError = new("InternalError", "서버 내부 오류가 발생했습니다.");
    }
}
