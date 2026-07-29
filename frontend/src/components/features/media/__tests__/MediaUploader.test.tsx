import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import MediaUploader from '@/components/features/media/MediaUploader'

const upload = vi.fn()

vi.mock('@/hooks/useMedia', () => ({
  useUploadMedia: () => ({ mutateAsync: upload, isPending: false, error: null }),
}))

describe('MediaUploader', () => {
  it('defaults to FamilyOnly and warns before selecting Public', () => {
    render(<MediaUploader purpose="MemberAvatar" ownerEntityId="owner-1" />)

    expect(screen.getByLabelText('僅限家人')).toBeChecked()
    expect(screen.queryByText('公開圖片')).not.toBeInTheDocument()
  })

  it('rejects unsupported and oversized files before upload', async () => {
    render(<MediaUploader purpose="Worship" ownerEntityId="owner-1" />)
    const input = screen.getByLabelText('選擇圖片')
    const invalid = new File(['text'], 'note.txt', { type: 'text/plain' })

    fireEvent.change(input, { target: { files: [invalid] } })
    expect(await screen.findByRole('alert')).toHaveTextContent('僅支援 JPEG、PNG 或 WebP 圖片')
    expect(upload).not.toHaveBeenCalled()
  })

  it('previews a valid image and submits its selected visibility', async () => {
    const onUploaded = vi.fn()
    const createObjectUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:preview')
    upload.mockResolvedValueOnce({ id: 'media-1' })

    render(<MediaUploader purpose="Worship" ownerEntityId="owner-1" onUploaded={onUploaded} />)
    fireEvent.click(screen.getByLabelText('公開'))
    expect(screen.getByRole('status')).toHaveTextContent('公開圖片可被未登入者讀取')

    const image = new File(['image'], 'worship.png', { type: 'image/png' })
    fireEvent.change(screen.getByLabelText('選擇圖片'), { target: { files: [image] } })
    expect(screen.getByAltText('待上傳圖片預覽')).toHaveAttribute('src', 'blob:preview')

    fireEvent.click(screen.getByRole('button', { name: '上傳圖片' }))
    await waitFor(() => expect(upload).toHaveBeenCalledWith(expect.objectContaining({ file: image, purpose: 'Worship', ownerEntityId: 'owner-1', visibility: 'Public' })))
    await waitFor(() => expect(onUploaded).toHaveBeenCalledTimes(1))
    createObjectUrl.mockRestore()
  })
})
