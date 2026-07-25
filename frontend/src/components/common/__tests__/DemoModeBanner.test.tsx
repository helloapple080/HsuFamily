import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import DemoModeBanner from '@/components/common/DemoModeBanner'

describe('DemoModeBanner', () => {
  it('清楚標示公開展示與不儲存資料', () => {
    render(<DemoModeBanner />)

    expect(screen.getByText('公開展示模式')).toBeVisible()
    expect(screen.getByText(/不會儲存/)).toBeVisible()
  })
})
