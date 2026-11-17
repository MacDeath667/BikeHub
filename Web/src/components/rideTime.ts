function parts(instant: Date, timeZone: string) {
  const values = new Intl.DateTimeFormat("en-CA", { timeZone, year: "numeric", month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hourCycle: "h23" }).formatToParts(instant);
  const get = (type: string) => values.find(p => p.type === type)!.value;
  return `${get("year")}-${get("month")}-${get("day")}T${get("hour")}:${get("minute")}`;
}
export function rideLocalTime(instant: string, zone: string) { return parts(new Date(instant), zone); }
export function rideInstant(local: string, zone: string) {
  if (!/^\d{4}-\d\d-\d\dT\d\d:\d\d$/.test(local)) throw Error("Укажите дату и время встречи.");
  const desired = Date.parse(local + "Z");
  if (!Number.isFinite(desired)) throw Error("Укажите корректную дату встречи.");
  let candidate = desired;
  for (let i = 0; i < 4; i++) candidate += desired - Date.parse(parts(new Date(candidate), zone) + "Z");
  const candidates = [-120, -60, -30, 0, 30, 60, 120].map(minutes => candidate + minutes * 60000)
    .filter(value => parts(new Date(value), zone) === local);
  if (!candidates.length) throw Error("В этом часовом поясе такого времени нет из-за перевода часов. Выберите другое время.");
  if (candidates.length > 1) throw Error("Это время повторяется при переводе часов. Выберите время вне перехода.");
  return new Date(candidates[0]).toISOString();
}
export function rideDate(instant: string, zone: string) {
  return new Intl.DateTimeFormat("ru-RU", { timeZone: zone, dateStyle: "long", timeStyle: "short" }).format(new Date(instant));
}
