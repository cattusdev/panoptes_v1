import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import {
  columnVisibilityFeature,
  rowSelectionFeature,
  tableFeatures,
  useTable,
  type ColumnDef,
  type ColumnVisibilityState,
  type RowData,
  type RowSelectionState,
} from '@tanstack/react-table'
import { ArrowDown, ArrowUp, ArrowUpDown, ChevronLeft, ChevronRight, Columns3 } from 'lucide-react'
import { OptionSelect } from '@/components/admin/OptionSelect'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { cn } from '@/lib/utils'

export type Column<T> = {
  key: string
  header: ReactNode
  cell: (row: T) => ReactNode
  className?: string
  // Ταξινόμηση από τον server με το key της στήλης (sort=key / sort=-key)
  sortable?: boolean
  // Μπορεί να κρυφτεί από το μενού "Columns" (χρειάζεται label)
  label?: string
}

// Η ταξινόμηση γίνεται στον server: την κρατάμε εμείς (prop sort), η TanStack κάνει επιλογή γραμμών και ορατότητα στηλών
const features = tableFeatures({ rowSelectionFeature, columnVisibilityFeature })
const noRows: never[] = []
const selectColumnId = '__select'

// "email" = αύξουσα, "-email" = φθίνουσα: η κατεύθυνση μιας στήλης
function sortDirection(sort: string | undefined, key: string): 'asc' | 'desc' | false {
  if (sort === key) return 'asc'
  if (sort === `-${key}`) return 'desc'
  return false
}

// Κοινός πίνακας για όλες τις λίστες: loading skeleton, empty state, clickable γραμμές, και προαιρετικά
//   sort/onSortChange: ταξινόμηση από τον server · columnVisibility: από το useColumnVisibility
//   bulkActions: επιλογή γραμμών (της σελίδας) και μπάρα ενεργειών για τις επιλεγμένες
export function DataTable<T extends RowData>({
  columns,
  rows,
  getRowId,
  isLoading,
  empty,
  onRowClick,
  rowLabel,
  sort,
  onSortChange,
  columnVisibility,
  bulkActions,
}: {
  columns: Column<T>[]
  rows: T[] | undefined
  getRowId: (row: T) => string | number
  isLoading?: boolean
  empty: ReactNode
  onRowClick?: (row: T) => void
  rowLabel?: (row: T) => string
  sort?: string
  onSortChange?: (sort: string) => void
  columnVisibility?: ColumnVisibilityState
  bulkActions?: (selectedIds: string[], clear: () => void) => ReactNode
}) {
  const { t } = useTranslation()
  const [rowSelection, setRowSelection] = useState<RowSelectionState>({})
  const data = rows ?? noRows
  const selectable = !!bulkActions

  const byKey = new Map(columns.map((column) => [column.key, column]))

  // Οι σελίδες φτιάχνουν τις στήλες σε κάθε render: οι ορισμοί της TanStack μένουν ίδιοι όσο δεν αλλάζει η "μορφή" τους,
  // και διαβάζουν το τρέχον header/cell από το ref (αλλιώς ο πίνακας ξαναχτίζει τα μοντέλα του σε κάθε render)
  const latest = useRef({ byKey, rowLabel })
  latest.current = { byKey, rowLabel }
  const shape = columns.map((column) => `${column.key}:${column.label ? 'h' : ''}`).join('|')

  const columnDefs = useMemo<ColumnDef<typeof features, T>[]>(() => {
    const defs: ColumnDef<typeof features, T>[] = shape.split('|').map((part) => {
      const [key, flags] = part.split(':')
      return {
        id: key,
        header: () => latest.current.byKey.get(key)?.header,
        cell: ({ row }) => latest.current.byKey.get(key)?.cell(row.original),
        enableHiding: flags.includes('h'),
      }
    })
    if (!selectable) return defs
    return [
      {
        id: selectColumnId,
        enableHiding: false,
        header: ({ table }) => (
          <Checkbox
            aria-label={t('table.selectPage')}
            checked={table.getIsAllPageRowsSelected() ? true : table.getIsSomePageRowsSelected() ? 'indeterminate' : false}
            onCheckedChange={(value) => table.toggleAllPageRowsSelected(value === true)}
          />
        ),
        cell: ({ row }) => (
          <Checkbox
            aria-label={t('table.selectRow', { name: latest.current.rowLabel?.(row.original) ?? row.id })}
            checked={row.getIsSelected()}
            onClick={(e) => e.stopPropagation()}
            onCheckedChange={(value) => row.toggleSelected(value === true)}
          />
        ),
      },
      ...defs,
    ]
  }, [shape, selectable, t])

  const table = useTable({
    features,
    columns: columnDefs,
    data,
    getRowId: (row) => String(getRowId(row)),
    state: { rowSelection, columnVisibility: columnVisibility ?? {} },
    onRowSelectionChange: setRowSelection,
  })

  // Η επιλογή αφορά τις γραμμές που φαίνονται: άλλη σελίδα ή φίλτρο -> κρατάμε μόνο όσες υπάρχουν ακόμα
  useEffect(() => {
    setRowSelection((current) => {
      const ids = new Set(data.map((row) => String(getRowId(row))))
      const kept = Object.keys(current).filter((id) => ids.has(id))
      return kept.length === Object.keys(current).length ? current : Object.fromEntries(kept.map((id) => [id, true as const]))
    })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data])

  const selectedIds = Object.keys(rowSelection)
  const visibleColumns = table.getVisibleLeafColumns()
  const className = (id: string) => (id === selectColumnId ? 'w-10 pr-0' : byKey.get(id)?.className)

  return (
    <div className="overflow-hidden rounded-xl border bg-card">
      {selectable && selectedIds.length > 0 && (
        <div
          role="region"
          aria-label={t('table.bulkActions')}
          className="flex flex-wrap items-center gap-2 border-b bg-primary/8 px-4 py-2 text-sm"
        >
          <span className="font-medium">{t('table.selected', { count: selectedIds.length })}</span>
          <Button variant="ghost" size="sm" onClick={() => setRowSelection({})}>
            {t('table.clearSelection')}
          </Button>
          <div className="ml-auto flex flex-wrap gap-2">{bulkActions(selectedIds, () => setRowSelection({}))}</div>
        </div>
      )}
      <Table>
        <TableHeader>
          {table.getHeaderGroups().map((group) => (
            <TableRow key={group.id} className="bg-white/[0.015] hover:bg-white/[0.015]">
              {group.headers.map((header) => {
                const sortable = !!onSortChange && !!byKey.get(header.column.id)?.sortable
                const sorted = sortDirection(sort, header.column.id)
                return (
                  <TableHead
                    key={header.id}
                    aria-sort={sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : undefined}
                    className={cn('h-11 px-4 font-semibold text-foreground', className(header.column.id))}
                  >
                    {sortable ? (
                      // asc <-> desc (χωρίς "καμία ταξινόμηση": η λίστα έχει πάντα μία, την προεπιλογή της σελίδας)
                      <button
                        type="button"
                        onClick={() => onSortChange(sorted === 'asc' ? `-${header.column.id}` : header.column.id)}
                        className="-mx-1.5 inline-flex items-center gap-1.5 rounded-md px-1.5 py-1 hover:bg-muted/60 focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                      >
                        <table.FlexRender header={header} />
                        {sorted === 'asc' ? (
                          <ArrowUp className="size-3.5" aria-hidden />
                        ) : sorted === 'desc' ? (
                          <ArrowDown className="size-3.5" aria-hidden />
                        ) : (
                          <ArrowUpDown className="size-3.5 text-muted-foreground" aria-hidden />
                        )}
                      </button>
                    ) : (
                      <table.FlexRender header={header} />
                    )}
                  </TableHead>
                )
              })}
            </TableRow>
          ))}
        </TableHeader>
        <TableBody>
          {isLoading &&
            Array.from({ length: 5 }, (_, i) => (
              <TableRow key={`skeleton-${i}`}>
                {visibleColumns.map((column) => (
                  <TableCell key={column.id} className="px-4 py-3.5">
                    <Skeleton className="h-5 w-full max-w-[180px]" />
                  </TableCell>
                ))}
              </TableRow>
            ))}

          {!isLoading && data.length === 0 && (
            <TableRow className="hover:bg-transparent">
              <TableCell colSpan={visibleColumns.length} className="p-0">
                {empty}
              </TableCell>
            </TableRow>
          )}

          {!isLoading &&
            table.getRowModel().rows.map((row) => (
              <TableRow
                key={row.id}
                aria-label={rowLabel?.(row.original)}
                aria-selected={selectable ? row.getIsSelected() : undefined}
                data-state={row.getIsSelected() ? 'selected' : undefined}
                onClick={onRowClick ? () => onRowClick(row.original) : undefined}
                className={cn(onRowClick && 'cursor-pointer')}
              >
                {row.getVisibleCells().map((cell) => (
                  <TableCell key={cell.id} className={cn('px-4 py-3', className(cell.column.id))}>
                    <table.FlexRender cell={cell} />
                  </TableCell>
                ))}
              </TableRow>
            ))}
        </TableBody>
      </Table>
    </div>
  )
}

// Το κουμπί "Columns": εμφάνιση/απόκρυψη των στηλών που έχουν label
export function ColumnsMenu<T>({
  columns,
  visibility,
  onToggle,
}: {
  columns: Column<T>[]
  visibility: ColumnVisibilityState
  onToggle: (key: string, visible: boolean) => void
}) {
  const { t } = useTranslation()
  const hideable = columns.filter((column) => column.label)
  if (hideable.length === 0) return null

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="outline" size="sm" className="h-9">
          <Columns3 /> {t('table.columns')}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-48">
        <DropdownMenuLabel className="text-xs text-muted-foreground">{t('table.showColumns')}</DropdownMenuLabel>
        {hideable.map((column) => (
          <DropdownMenuCheckboxItem
            key={column.key}
            checked={visibility[column.key] !== false}
            onCheckedChange={(checked) => onToggle(column.key, checked === true)}
            onSelect={(e) => e.preventDefault()}
          >
            {column.label}
          </DropdownMenuCheckboxItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}

const pageSizes = [10, 20, 50, 100] as const

export function TablePagination({
  page,
  pageSize,
  total,
  onPageChange,
  onPageSizeChange,
}: {
  page: number
  pageSize: number
  total: number
  onPageChange: (page: number) => void
  // Αν δοθεί: επιλογή "Rows per page"
  onPageSizeChange?: (pageSize: number) => void
}) {
  const { t } = useTranslation()
  if (total === 0) return null
  const from = (page - 1) * pageSize + 1
  const to = Math.min(page * pageSize, total)
  const lastPage = Math.max(1, Math.ceil(total / pageSize))

  return (
    <div className="mt-3 flex flex-wrap items-center justify-between gap-3 text-sm text-muted-foreground">
      <span>{t('table.range', { from, to, total })}</span>
      <div className="flex flex-wrap items-center gap-4">
        {onPageSizeChange && (
          <label className="flex items-center gap-2">
            <span>{t('table.rowsPerPage')}</span>
            <OptionSelect
              ariaLabel={t('table.rowsPerPage')}
              value={String(pageSize)}
              onChange={(value) => onPageSizeChange(Number(value))}
              options={pageSizes.map((size) => ({ value: String(size), label: String(size) }))}
              className="h-8 w-[4.5rem]"
            />
          </label>
        )}
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
    </div>
  )
}
