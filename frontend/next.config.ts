import type { NextConfig } from "next";

const isDemoMode = process.env.NEXT_PUBLIC_DEMO_MODE === "true";

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
    };

export default nextConfig;
