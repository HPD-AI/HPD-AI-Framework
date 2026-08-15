using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace HPD.Gateway.Admission.Redis;

internal static class GatewayRedisScripts
{
    internal const string AcquireText = """
local t = redis.call('TIME')
local now = (tonumber(t[1]) * 1000) + math.floor(tonumber(t[2]) / 1000)
local algorithm = tonumber(@Algorithm)
local limit = tonumber(@Limit)
local tokensPer = tonumber(@Tokens)
local window = tonumber(@Window)
local segmentCount = tonumber(@Segments)
local permits = tonumber(@Permits)

if limit < 1 or limit > 100000000 or permits < 1 or permits > limit or
   window < 100 or window > 86400000 or
   (algorithm == 0 and (window < 1000 or tokensPer ~= 0 or segmentCount ~= 0)) or
   (algorithm == 1 and (window < 1000 or tokensPer ~= 0 or segmentCount < 2 or segmentCount > 64 or window % segmentCount ~= 0)) or
   (algorithm == 2 and (tokensPer < 1 or tokensPer > 100000000 or segmentCount ~= 0)) then
  return redis.error_reply('HPD_INVALID')
end

local exists = redis.call('EXISTS', @Key) == 1
if exists then
  if redis.call('HGET', @Key, 'behavior') ~= @Behavior or
     tonumber(redis.call('HGET', @Key, 'algorithm')) ~= algorithm or
     tonumber(redis.call('HGET', @Key, 'limit')) ~= limit or
     tonumber(redis.call('HGET', @Key, 'tokensPer')) ~= tokensPer or
     tonumber(redis.call('HGET', @Key, 'window')) ~= window or
     tonumber(redis.call('HGET', @Key, 'segments')) ~= segmentCount then
    return {2, -1, -1, -1, tostring(now), -1}
  end
else
  redis.call('HSET', @Key, 'behavior', @Behavior, 'algorithm', algorithm, 'limit', limit,
    'tokensPer', tokensPer, 'window', window, 'segments', segmentCount)
end

local previous = tonumber(redis.call('HGET', @Key, 'last') or now)
if previous > now then now = previous end
redis.call('HSET', @Key, 'last', now)

if algorithm == 0 then
  local start = math.floor(now / window) * window
  local storedStart = tonumber(redis.call('HGET', @Key, 'windowStart') or -1)
  local used = tonumber(redis.call('HGET', @Key, 'used') or 0)
  if storedStart ~= start then used = 0 end
  local acquired = used + permits <= limit
  if acquired then used = used + permits end
  local expiry = start + window
  redis.call('HSET', @Key, 'windowStart', start, 'used', used, 'expiry', expiry)
  redis.call('PEXPIREAT', @Key, expiry)
  local remaining = limit - used
  local delay = math.max(1, expiry - now)
  if acquired then return {0, remaining, -1, delay, tostring(now), expiry} end
  return {1, remaining, delay, delay, tostring(now), expiry}
end

if algorithm == 1 then
  local width = window / segmentCount
  local epoch = math.floor(now / width)
  local oldest = epoch - segmentCount + 1
  local entries = {}
  local used = 0
  local newest = epoch
  for i = 0, segmentCount - 1 do
    local e = tonumber(redis.call('HGET', @Key, 'e' .. i) or -1)
    local c = tonumber(redis.call('HGET', @Key, 'c' .. i) or 0)
    if e < oldest or e > epoch then e = -1; c = 0 end
    if c > 0 then
      used = used + c
      if e > newest then newest = e end
      table.insert(entries, {e, c})
    end
    redis.call('HSET', @Key, 'e' .. i, e, 'c' .. i, c)
  end
  local slot = epoch % segmentCount
  local slotEpoch = tonumber(redis.call('HGET', @Key, 'e' .. slot) or -1)
  local slotCount = tonumber(redis.call('HGET', @Key, 'c' .. slot) or 0)
  if slotEpoch ~= epoch then slotEpoch = epoch; slotCount = 0 end
  local acquired = used + permits <= limit
  if acquired then slotCount = slotCount + permits; used = used + permits end
  redis.call('HSET', @Key, 'e' .. slot, slotEpoch, 'c' .. slot, slotCount)
  if slotCount > 0 and slotEpoch > newest then newest = slotEpoch end
  local reset = math.max(1, ((newest + segmentCount) * width) - now)
  local expiry = now + reset
  redis.call('HSET', @Key, 'expiry', expiry)
  redis.call('PEXPIREAT', @Key, expiry)
  local remaining = limit - used
  if acquired then return {0, remaining, -1, reset, tostring(now), expiry} end
  table.sort(entries, function(a, b) return a[1] < b[1] end)
  local retained = used
  local retry = reset
  for _, item in ipairs(entries) do
    retained = retained - item[2]
    if limit - retained >= permits then
      retry = math.max(1, ((item[1] + segmentCount) * width) - now)
      break
    end
  end
  return {1, remaining, retry, reset, tostring(now), expiry}
end

local available
local refill
local remainder
if exists and redis.call('HEXISTS', @Key, 'available') == 1 then
  available = tonumber(redis.call('HGET', @Key, 'available'))
  refill = tonumber(redis.call('HGET', @Key, 'refill'))
  remainder = tonumber(redis.call('HGET', @Key, 'remainder'))
else
  available = limit
  refill = now
  remainder = 0
end
local elapsed = math.max(0, now - refill)
local numerator = (elapsed * tokensPer) + remainder
local added = math.floor(numerator / window)
available = math.min(limit, available + added)
if available == limit then remainder = 0 else remainder = numerator % window end
refill = now
local acquired = available >= permits
if acquired then available = available - permits end
local function delay(missing)
  if missing <= 0 then return 1 end
  return math.max(1, math.floor(((missing * window) - remainder + tokensPer - 1) / tokensPer))
end
local reset = delay(limit - available)
local expiry = now + reset
redis.call('HSET', @Key, 'available', available, 'refill', refill, 'remainder', remainder, 'expiry', expiry)
redis.call('PEXPIREAT', @Key, expiry)
if acquired then return {0, available, -1, reset, tostring(now), expiry} end
return {1, available, delay(permits - available), reset, tostring(now), expiry}
""";

    internal static readonly LuaScript Acquire = LuaScript.Prepare(AcquireText);
    internal static readonly string AcquireSha256 = Hash(Acquire.ExecutableScript);
    internal static readonly string ObserveSha256 = Hash("HGETALL/v1");

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
