import { describe, expect, it } from 'vitest'
import { demoEvents, demoItineraries, demoMembers } from '../fixtures'

describe('公開展示資料', () => {
  it('全部使用虛構內容並凍結', () => {
    const serialized = JSON.stringify({ demoEvents, demoItineraries, demoMembers })

    expect(serialized).not.toContain('許浴宸')
    expect(serialized).toContain('example.invalid')
    expect(Object.isFrozen(demoEvents)).toBe(true)
    expect(Object.isFrozen(demoItineraries)).toBe(true)
    expect(Object.isFrozen(demoMembers)).toBe(true)
    expect(demoMembers.every(Object.isFrozen)).toBe(true)
  })
})
