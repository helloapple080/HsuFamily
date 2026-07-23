import { Lunar, Solar } from 'lunar-javascript'

/// 國曆轉農曆
export function solarToLunar(year: number, month: number, day: number) {
  const solar = Solar.fromYmd(year, month, day)
  const lunar = solar.getLunar()

  const isLeapMonth = lunar.getMonth() < 0 // In lunar-javascript negative month means leap month, but getMonthInChinese might hide this. Actually lunar.getMonth() handles leap. Let's use getMonth() signature.
  const absMonth = Math.abs(lunar.getMonth())

  return {
    year: lunar.getYear(),
    month: absMonth,
    day: lunar.getDay(),
    isLeapMonth: isLeapMonth, 
    monthChinese: lunar.getMonthInChinese(),
    dayChinese: lunar.getDayInChinese(),
    yearGanZhi: lunar.getYearInGanZhi(),   // 天干地支年份
    displayText: `農曆${isLeapMonth ? '閏' : ''}${lunar.getMonthInChinese()}月${lunar.getDayInChinese()}`
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
    day: solar.getDay()
  }
}

/// 取得某月每日的農曆資訊
export function getMonthLunarInfo(year: number, month: number) {
  const daysInMonth = new Date(year, month, 0).getDate()
  const result = []

  for (let day = 1; day <= daysInMonth; day++) {
    const solar = Solar.fromYmd(year, month, day)
    const lunar = solar.getLunar()

    result.push({
      solarDate: `${year}-${month.toString().padStart(2, '0')}-${day.toString().padStart(2, '0')}`,
      lunarMonth: lunar.getMonthInChinese(),
      lunarDay: lunar.getDayInChinese(),
      // 每月初一顯示月份，其餘顯示日
      displayText: lunar.getDay() === 1
        ? `${lunar.getMonth() < 0 ? '閏' : ''}${lunar.getMonthInChinese()}月`
        : lunar.getDayInChinese(),
      festivals: lunar.getFestivals(),      // 農曆節日
      solarFestivals: solar.getFestivals(), // 國曆節日
      jieQi: lunar.getJieQi(),             // 節氣
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
