-- 세션 발급 레이트리밋 카운터 (src/ratelimit.ts D1Limiter, 2026-09-26).
-- Workers 레이트리밋 바인딩은 막지 않았고 인스턴스 카운터는 인스턴스마다 따로 세서 느슨했다 - 세션 발급은 요청마다 밸브
-- API 를 부르므로 여기서 정확히 센다. 키는 "ip:<접속 IP>". 창이 끝난 행은 가끔 지운다.
CREATE TABLE IF NOT EXISTS rate_limits (
  key          TEXT PRIMARY KEY,
  window_start INTEGER NOT NULL,
  count        INTEGER NOT NULL
);
