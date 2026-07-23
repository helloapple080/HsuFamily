import { clsx, type ClassValue } from "clsx"
import { twMerge } from "tailwind-merge"

/// shadcn/ui 工具函式 — 合併 Tailwind class names
export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}
