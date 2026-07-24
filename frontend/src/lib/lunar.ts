import { Lunar, Solar } from 'lunar-javascript'

const lunarMonths = [
  '正', '二', '三', '四', '五', '六', '七', '八', '九', '十', '冬', '臘',
] as const

const lunarDays = [
  '初一', '初二', '初三', '初四', '初五', '初六', '初七', '初八', '初九', '初十',
  '十一', '十二', '十三', '十四', '十五', '十六', '十七', '十八', '十九', '二十',
  '廿一', '廿二', '廿三', '廿四', '廿五', '廿六', '廿七', '廿八', '廿九', '三十',
] as const

const taiwanSolarFestivals: Readonly<Record<string, readonly string[]>> = {
  '01-01': ['元旦'],
  '02-28': ['和平紀念日'],
  '04-04': ['兒童節'],
  '05-01': ['勞動節'],
  '10-10': ['國慶日'],
  '12-25': ['行憲紀念日'],
}

const taiwanLunarFestivals: Readonly<Record<string, readonly string[]>> = {
  '01-01': ['春節'],
  '01-15': ['元宵節'],
  '05-05': ['端午節'],
  '07-15': ['中元節'],
  '08-15': ['中秋節'],
  '09-09': ['重陽節'],
}

function festivalKey(month: number, day: number): string {
  return `${month.toString().padStart(2, '0')}-${day.toString().padStart(2, '0')}`
}

/// 取得臺灣地區指定國曆節日，回傳值可安全修改。
export function getTaiwanSolarFestivals(month: number, day: number): string[] {
  return [...(taiwanSolarFestivals[festivalKey(month, day)] ?? [])]
}

/// 取得臺灣地區指定農曆節日，回傳值可安全修改。
export function getTaiwanLunarFestivals(month: number, day: number): string[] {
  return [...(taiwanLunarFestivals[festivalKey(month, day)] ?? [])]
}

/// 以專案規範格式化經驗證的農曆日期。
export function formatLunarDate(month: number, day: number, isLeapMonth = false): string {
  if (
    !Number.isInteger(month) ||
    month < 1 ||
    month > 12 ||
    !Number.isInteger(day) ||
    day < 1 ||
    day > 30
  ) {
    throw new Error('農曆日期不合法')
  }

  return `農曆${isLeapMonth ? '閏' : ''}${lunarMonths[month - 1]}月${lunarDays[day - 1]}`
}

/// 國曆轉農曆
export function solarToLunar(year: number, month: number, day: number) {
  const solar = Solar.fromYmd(year, month, day)
  const lunar = solar.getLunar()

  // lunar-javascript 以負數月份表示閏月。
  const isLeapMonth = lunar.getMonth() < 0
  const absMonth = Math.abs(lunar.getMonth())
  const lunarDay = lunar.getDay()

  return {
    year: lunar.getYear(),
    month: absMonth,
    day: lunarDay,
    isLeapMonth,
    monthChinese: lunarMonths[absMonth - 1],
    dayChinese: lunarDays[lunarDay - 1],
    yearGanZhi: lunar.getYearInGanZhi(),
    displayText: formatLunarDate(absMonth, lunarDay, isLeapMonth),
  }
}

/// 農曆轉國曆
export function lunarToSolar(year: number, month: number, day: number, isLeapMonth: boolean = false) {
  const lunarMonth = isLeapMonth ? -Math.abs(month) : Math.abs(month)
  const lunar = Lunar.fromYmd(year, lunarMonth, day)
  const solar = lunar.getSolar()

  return {
    year: solar.getYear(),
    month: solar.getMonth(),
    day: solar.getDay(),
  }
}

/// 取得某月每日的農曆資訊
export function getMonthLunarInfo(year: number, month: number) {
  const daysInMonth = new Date(year, month, 0).getDate()
  const result = []

  for (let day = 1; day <= daysInMonth; day++) {
    const solar = Solar.fromYmd(year, month, day)
    const lunar = solar.getLunar()

    const lunarMonth = Math.abs(lunar.getMonth())

    result.push({
      solarDate: `${year}-${month.toString().padStart(2, '0')}-${day.toString().padStart(2, '0')}`,
      lunarMonth: lunarMonths[lunarMonth - 1],
      lunarDay: lunarDays[lunar.getDay() - 1],
      isLeapMonth: lunar.getMonth() < 0,
      // 每月初一顯示月份，其餘顯示日
      displayText: lunar.getDay() === 1
        ? `${lunar.getMonth() < 0 ? '閏' : ''}${lunarMonths[lunarMonth - 1]}月`
        : lunarDays[lunar.getDay() - 1],
      festivals: lunar.getMonth() < 0
        ? []
        : getTaiwanLunarFestivals(lunarMonth, lunar.getDay()),
      solarFestivals: getTaiwanSolarFestivals(month, day),
      jieQi: lunar.getJieQi(),
    })
  }

  return result
}

/// 今年的國曆生日日期（從農曆生日算出）
export function getLunarBirthdayThisYear(
  lunarMonth: number,
  lunarDay: number,
  isLeapMonth: boolean = false
): Date {
  const currentYear = new Date().getFullYear()
  // lunar-javascript 的年份用農曆年，需要根據當前國曆年推算
  const month = isLeapMonth ? -Math.abs(lunarMonth) : Math.abs(lunarMonth)
  const lunar = Lunar.fromYmd(currentYear, month, lunarDay)
  const solar = lunar.getSolar()
  return new Date(solar.getYear(), solar.getMonth() - 1, solar.getDay())
}

/// 取得本月的初一十五國曆日期
export function getWorshipDaysThisMonth(year: number, month: number): Date[] {
  const dates: Date[] = []

  // 找出本月包含的農曆月份
  for (let day = 1; day <= 31; day++) {
    try {
      // month here is 1-12
      const solar = Solar.fromYmd(year, month, day)
      const lunar = solar.getLunar()

      if (lunar.getDay() === 1 || lunar.getDay() === 15) {
        dates.push(new Date(year, month - 1, day))
      }
    } catch {
      break // 超出該月天數
    }
  }

  return dates
}
