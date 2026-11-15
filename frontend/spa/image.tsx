import type { ComponentProps } from 'react'

interface ImageProps extends ComponentProps<'img'> {
  unoptimized?: boolean
}

/// 私人圖片直接使用受保護的同源 API，不經圖片轉送代理。
export default function Image({ src, alt, width, height, className }: ImageProps) {
  // eslint-disable-next-line @next/next/no-img-element
  return <img src={src} alt={alt} width={width} height={height} className={className} loading="lazy" decoding="async" />
}
