import type { NextConfig } from "next";

const isDemoMode = process.env.NEXT_PUBLIC_DEMO_MODE === "true";
const isHostedMode = !isDemoMode && process.env.NEXT_PUBLIC_PORTAL_MODE === "supabase";

const nextConfig: NextConfig = isDemoMode
  ? {
      poweredByHeader: false,
      output: "export",
      basePath: "/HsuFamily",
      trailingSlash: true,
      images: { unoptimized: true },
    }
  : {
      poweredByHeader: false,
      output: "standalone",
      ...(isHostedMode && {
        pageExtensions: ["hosted.ts", "tsx", "ts", "jsx", "js"],
      }),
    };

export default nextConfig;
