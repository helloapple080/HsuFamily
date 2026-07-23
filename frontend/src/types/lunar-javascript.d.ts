declare module 'lunar-javascript' {
  export class Lunar {
    static fromYmd(year: number, month: number, day: number): Lunar
    getYear(): number
    getMonth(): number
    getDay(): number
    getMonthInChinese(): string
    getDayInChinese(): string
    getYearInGanZhi(): string
    getSolar(): Solar
    getFestivals(): string[]
    getJieQi(): string
  }

  export class Solar {
    static fromYmd(year: number, month: number, day: number): Solar
    getLunar(): Lunar
    getYear(): number
    getMonth(): number
    getDay(): number
    getFestivals(): string[]
  }

  export class HolidayUtil {
    static getHoliday(year: number, month: number, day: number): unknown
  }
}
