import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";

const geistSans = Geist({
  variable: "--font-geist-sans",
  subsets: ["latin"],
});

const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

export const metadata: Metadata = {
  metadataBase: new URL("https://dailygate.local"),
  title: {
    default: "DailyGate — ежедневный допуск",
    template: "%s · DailyGate",
  },
  description: "Закрытая панель ежедневного тестирования сотрудников и состояния рабочих компьютеров.",
  openGraph: {
    title: "DailyGate — ежедневный допуск сотрудников",
    description: "Тесты, устройства и контроль ежедневного допуска в одной защищённой панели.",
    images: ["/og.png"],
  },
  twitter: {
    card: "summary_large_image",
    title: "DailyGate — ежедневный допуск сотрудников",
    description: "Тесты, устройства и контроль ежедневного допуска в одной защищённой панели.",
    images: ["/og.png"],
  },
  icons: {
    icon: "/favicon.svg",
    shortcut: "/favicon.svg",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ru">
      <body
        className={`${geistSans.variable} ${geistMono.variable} antialiased`}
      >
        {children}
      </body>
    </html>
  );
}
