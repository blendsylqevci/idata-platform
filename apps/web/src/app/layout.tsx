import type { ReactNode } from "react";

export const metadata = {
  title: "iData+ — Walking Skeleton",
  description: "Increment 1 — web → api → db",
};

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="sq">
      <body>{children}</body>
    </html>
  );
}
