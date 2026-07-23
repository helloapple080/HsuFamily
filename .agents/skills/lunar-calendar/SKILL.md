---
name: lunar-calendar
description: >-
  農曆日期轉換與顯示邏輯。包含 lunar-javascript 使用方式、農曆生日計算、節日判定。
  Use when implementing lunar calendar features, converting dates, or creating worship reminders.
---

# 農曆行事曆技能

## 核心套件: lunar-javascript
```typescript
import { Lunar, Solar, HolidayUtil } from 'lunar-javascript'
```

## 國曆 ↔ 農曆 轉換

### 國曆轉農曆
```typescript
function solarToLunar(year: number, month: number, day: number) {
  const solar = Solar.fromYmd(year, month, day)
  const lunar = solar.getLunar()

  return {
    year: lunar.getYear(),
    month: lunar.getMonth(),
    day: lunar.getDay(),
    isLeapMonth: lunar.getMonth() !== lunar.getMonthInChinese().length, // 需額外判斷
    monthChinese: lunar.getMonthInChinese(),
    dayChinese: lunar.getDayInChinese(),
    yearGanZhi: lunar.getYearInGanZhi(),   // 天干地支年份
    displayText: `農曆${lunar.getMonthInChinese()}月${lunar.getDayInChinese()}`
  }
}
```

### 農曆轉國曆
```typescript
function lunarToSolar(year: number, month: number, day: number, isLeapMonth: boolean = false) {
  const lunar = Lunar.fromYmd(year, month, day)
  const solar = lunar.getSolar()

  return {
    year: solar.getYear(),
    month: solar.getMonth(),
    day: solar.getDay()
  }
}
```

## 行事曆月份顯示

### 取得某月每日的農曆資訊
```typescript
function getMonthLunarInfo(year: number, month: number) {
  const daysInMonth = new Date(year, month, 0).getDate()
  const result = []

  for (let day = 1; day <= daysInMonth; day++) {
    const solar = Solar.fromYmd(year, month, day)
    const lunar = solar.getLunar()

    result.push({
      solarDate: `${year}-${month}-${day}`,
      lunarMonth: lunar.getMonthInChinese(),
      lunarDay: lunar.getDayInChinese(),
      // 每月初一顯示月份，其餘顯示日
      displayText: lunar.getDay() === 1
        ? `${lunar.getMonthInChinese()}月`
        : lunar.getDayInChinese(),
      festivals: lunar.getFestivals(),      // 農曆節日
      solarFestivals: solar.getFestivals(), // 國曆節日
      jieQi: lunar.getJieQi(),             // 節氣
    })
  }

  return result
}
```

## 節日判定

### 台灣傳統節日對照表
| 節日 | 農曆/國曆 | 日期 |
|------|----------|------|
| 春節 | 農曆 | 正月初一 |
| 元宵節 | 農曆 | 正月十五 |
| 清明節 | 國曆/節氣 | 約 4/4-4/6 |
| 端午節 | 農曆 | 五月初五 |
| 中元節 | 農曆 | 七月十五 |
| 中秋節 | 農曆 | 八月十五 |
| 重陽節 | 農曆 | 九月初九 |
| 冬至 | 國曆/節氣 | 約 12/21-12/23 |

## 農曆生日計算

### 今年的國曆生日日期（從農曆生日算出）
```typescript
function getLunarBirthdayThisYear(
  lunarMonth: number,
  lunarDay: number,
  isLeapMonth: boolean = false
): Date {
  const currentYear = new Date().getFullYear()
  // lunar-javascript 的年份用農曆年，需要根據當前國曆年推算
  const lunar = Lunar.fromYmd(currentYear, lunarMonth, lunarDay)
  const solar = lunar.getSolar()
  return new Date(solar.getYear(), solar.getMonth() - 1, solar.getDay())
}
```

## 拜拜日計算

### 取得本月的初一十五國曆日期
```typescript
function getWorshipDaysThisMonth(year: number, month: number): Date[] {
  const dates: Date[] = []

  // 找出本月包含的農曆月份
  for (let day = 1; day <= 31; day++) {
    try {
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
```

## 顯示格式規範
- 農曆日期：`農曆X月X日`（如：農曆正月初一）
- 閏月日期：`農曆閏X月X日`（如：農曆閏六月十二）
- 天干地支：`甲子年`（選用）
- 節氣：`立春`、`雨水`（unicode 中文）
