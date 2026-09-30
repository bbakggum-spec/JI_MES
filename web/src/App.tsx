import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { App as AntApp, ConfigProvider } from 'antd'
import koKR from 'antd/locale/ko_KR'
import dayjs from 'dayjs'
import 'dayjs/locale/ko'
import customParseFormat from 'dayjs/plugin/customParseFormat'
import { createBrowserRouter } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { ApiError } from './api/client'
import { AuthProvider } from './auth/AuthProvider'
import RequireAuth from './auth/RequireAuth'
import AppLayout from './layout/AppLayout'
import LoginPage from './pages/LoginPage'
import { RealtimeProvider } from './realtime/RealtimeProvider'

dayjs.locale('ko')
dayjs.extend(customParseFormat)

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // 권한·검증 오류는 다시 시도해도 같으므로 네트워크·서버 오류만 재시도
      retry: (count, error) => count < 2 && !(error instanceof ApiError && error.status >= 400 && error.status < 500),
      refetchOnWindowFocus: false,
    },
  },
})

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  { path: '*', element: <RequireAuth><AppLayout /></RequireAuth> },
])

export default function App() {
  return (
    <ConfigProvider locale={koKR} theme={{ token: { colorPrimary: '#d4380d' } }}>
      <AntApp>
        <QueryClientProvider client={queryClient}>
          <AuthProvider>
            <RealtimeProvider>
              <RouterProvider router={router} />
            </RealtimeProvider>
          </AuthProvider>
        </QueryClientProvider>
      </AntApp>
    </ConfigProvider>
  )
}
