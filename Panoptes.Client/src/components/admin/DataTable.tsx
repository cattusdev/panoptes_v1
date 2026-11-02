import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { ChevronLeft, ChevronRight } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { cn } from '@/lib/utils'

export type Column<T> = {
  key: string
  header: ReactNode
  cell: (row: T) => ReactNode
  className?: string
}

// Κοινός πίνακας για όλες τις λίστες του admin: loading skeleton, empty state, clickable γραμμές
export function DataTable<T>({
  columns,
  rows,
  getRowId,
  isLoading,
  empty,
  onRowClick,
  rowLabel,
}: {
  columns: Column<T>[]
  rows: T[] | undefined
  getRowId: (row: T) => string | number
  isLoading?: boolean
  empty: ReactNode
  onRowClick?: (row: T) => void
  rowLabel?: (row: T) => string
}) {
  return (
    <div className="overflow-hidden rounded-xl border bg-card">
      <Table>
        <TableHeader>
          <TableRow className="bg-white/[0.015] hover:bg-white/[0.015]">
            {columns.map((column) => (
              <TableHead key={column.key} className={cn('h-11 px-4 font-semibold text-foreground', column.className)}>
                {column.header}
              </TableHead>
            ))}
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading &&
            Array.from({ length: 5 }, (_, i) => (
              <TableRow key={`skeleton-${i}`}>
                {columns.map((column) => (
                  <TableCell key={column.key} className="px-4 py-3.5">
                    <Skeleton className="h-5 w-full max-w-[180px]" />
                  </TableCell>
                ))}
              </TableRow>
            ))}

          {!isLoading && rows?.length === 0 && (
            <TableRow className="hover:bg-transparent">
              <TableCell colSpan={columns.length} className="p-0">
                {empty}
              </TableCell>
            </TableRow>
          )}

          {!isLoading &&
            rows?.map((row) => (
              <TableRow
                key={getRowId(row)}
                aria-label={rowLabel?.(row)}
                onClick={onRowClick ? () => onRowClick(row) : undefined}
                className={cn(onRowClick && 'cursor-pointer')}
              >
                {columns.map((column) => (
                  <TableCell key={column.key} className={cn('px-4 py-3', column.className)}>
                    {column.cell(row)}
                  </TableCell>
                ))}
              </TableRow>
            ))}
        </TableBody>
      </Table>
    </div>
  )
}

export function TablePagination({
  page,
  pageSize,
  total,
  onPageChange,
}: {
  page: number
  pageSize: number
  total: number
  onPageChange: (page: number) => void
}) {
  const { t } = useTranslation()
  if (total === 0) return null
  const from = (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, total)
  const lastPage = Math.max(1, Math.ceil(total / pageSize))

  return (
    <div className="mt-3 flex items-center justify-between text-sm text-muted-foreground">
      <span>{t('table.range', { from, to, total })}</span>
      <div className="flex items-center gap-1">
        <Button variant="outline" size="icon-sm" aria-label={t('table.previousPage')} disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
          <ChevronLeft />
        </Button>
        <span className="px-2">{t('table.page', { page, last: lastPage })}</span>
        <Button variant="outline" size="icon-sm" aria-label={t('table.nextPage')} disabled={page >= lastPage} onClick={() => onPageChange(page + 1)}>
          <ChevronRight />
        </Button>
      </div>
    </div>
  )
}
