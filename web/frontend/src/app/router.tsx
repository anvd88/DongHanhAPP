import type { ComponentType } from 'react'
import { Route, Routes } from 'react-router-dom'
import { AppShell } from '@/shell/AppShell'
import { LoginPage } from '@/auth/LoginPage'
import { LandingRedirect, RequireAuth, RequirePermission } from '@/auth/guards'
import { ALL_ROUTES } from '@/nav/navigation'
import { NotFoundPage } from '@/pages/system-states'

import { BlankLanding, DashboardPage, ReportsPage, WorklistPage } from '@/pages/dieu-hanh'
import {
  CustomersPage,
  DebtsPage,
  DeliveryPage,
  GiaCongPage,
  ProductsPage,
  ReturnsPage,
  SalesDocumentDetailPage,
  SalesDocumentsPage,
} from '@/pages/ban-hang'
import { PurchasesPage, SuppliersPage } from '@/pages/mua-hang'
import {
  CashCollectionsPage,
  CashFundPage,
  CashVouchersPage,
  PayoutVouchersPage,
} from '@/pages/ke-toan'
import {
  BankAccountsPage,
  DirectoryPage,
  HrEmployeesPage,
  MySpacePage,
  PayrollPage,
  PenaltiesPage,
  TalentPage,
} from '@/pages/nhan-su'
import {
  AttendanceAdminPage,
  AttendanceStationPage,
  CompanyTimesheetPage,
  MyTimesheetPage,
  ShiftsPage,
} from '@/pages/cham-cong'
import { ApprovalsPage, MyRequestsPage, RequestsAdminPage, TasksPage } from '@/pages/cong-viec'
import { FeedbackPage, HelpPage, PortalPage, SurveysPage } from '@/pages/cong-thong-tin'
import {
  AuditPage,
  DevicesPage,
  NotificationsPage,
  ProfilePage,
  ReleasesPage,
  SettingsPage,
  UsersPage,
} from '@/pages/he-thong'

/**
 * Bảng đăng ký màn hình: địa chỉ → thành phần.
 *
 * Luật quyền không khai lại ở đây mà đọc từ nav/navigation.ts, nên menu và chốt route luôn khớp
 * nhau; thêm màn hình chỉ phải sửa một nơi.
 */
const SCREENS: Record<string, ComponentType> = {
  '/dashboard': DashboardPage,
  '/viec-can-lam': WorklistPage,
  '/bao-cao': ReportsPage,

  '/ban-hang': SalesDocumentsPage,
  '/ban-hang/:id': SalesDocumentDetailPage,
  '/giao-hang': DeliveryPage,
  '/hang-tra-ve': ReturnsPage,
  '/khach-hang': CustomersPage,
  '/cong-no': DebtsPage,
  '/danh-muc-hang': ProductsPage,
  '/gia-cong': GiaCongPage,

  '/mua-hang': PurchasesPage,
  '/nha-cung-cap': SuppliersPage,

  '/thu-chi': CashVouchersPage,
  '/quy-tien-mat': CashFundPage,
  '/lenh-thu-tien': CashCollectionsPage,
  '/phieu-chi': PayoutVouchersPage,

  '/nhan-su': MySpacePage,
  '/danh-ba': DirectoryPage,
  '/quanly-nhansu': HrEmployeesPage,
  '/bang-luong': PayrollPage,
  '/phat': PenaltiesPage,
  '/phat-trien': TalentPage,
  '/tai-khoan-ngan-hang': BankAccountsPage,

  '/chamcong': AttendanceStationPage,
  '/bang-cong': MyTimesheetPage,
  '/ca-lam': ShiftsPage,
  '/quanly-bangcong': CompanyTimesheetPage,
  '/ql-chamcong': AttendanceAdminPage,

  '/cong-viec': TasksPage,
  '/dontu': MyRequestsPage,
  '/pheduyet': ApprovalsPage,
  '/quanly-dontu': RequestsAdminPage,

  '/cong-thong-tin': PortalPage,
  '/khao-sat': SurveysPage,
  '/tro-giup': HelpPage,
  '/phan-hoi': FeedbackPage,

  '/nguoi-dung': UsersPage,
  '/caidat': SettingsPage,
  '/tai-apk': ReleasesPage,
  '/saoluu': AuditPage,
  '/thiet-bi': DevicesPage,
  '/thong-bao': NotificationsPage,
  '/ho-so': ProfilePage,
}

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/dang-nhap" element={<LoginPage />} />

      <Route
        element={
          <RequireAuth>
            <AppShell />
          </RequireAuth>
        }
      >
        <Route path="/" element={<LandingRedirect />} />

        {ALL_ROUTES.map((route) => {
          const Screen = SCREENS[route.path] ?? BlankLanding
          return (
            <Route
              key={route.path}
              path={route.path}
              element={
                <RequirePermission requires={route.requires} requiresAny={route.requiresAny}>
                  <Screen />
                </RequirePermission>
              }
            />
          )
        })}

        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}
