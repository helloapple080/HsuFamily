import { describe, expect, it } from 'vitest'

describe('test harness', () => {
  it('runs with a browser-like document', () => {
    expect(document).toBeDefined()
  })
})
