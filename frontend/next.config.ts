import type { NextConfig } from "next";

const isDemoMode = process.env.NEXT_PUBLIC_DEMO_MODE === "true";

const nextConfig: NextConfig = isDemoMode
  ? {
      poweredByHeader: false,
      output: "export",
      basePath: "/HsuFamily",
      trailingSlash: true,
      images: { unoptimized: true },
      typescript: { tsconfigPath: "tsconfig.pages.json" },
    }
  : {
      poweredByHeader: false,
      output: "standalone",
      // 公開靜態展示的設定不註冊 .hosted.ts 私人路由。
      pageExtensions: ["hosted.ts", "tsx", "ts", "jsx", "js"],
    };

export default nextConfig;
