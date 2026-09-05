import { Kbd, Modal } from '@/ui'

const GROUPS: { title: string; items: { keys: string[]; label: string }[] }[] = [
  {
    title: 'Chung',
    items: [
      { keys: ['Ctrl', 'K'], label: 'Tìm và mở màn hình' },
      { keys: ['?'], label: 'Bảng phím tắt này' },
      { keys: ['Esc'], label: 'Đóng hộp thoại, ngăn kéo hoặc chứng từ' },
    ],
  },
  {
    title: 'Chứng từ',
    items: [
      { keys: ['Ctrl', 'S'], label: 'Lưu' },
      { keys: ['Ctrl', 'Shift', 'S'], label: 'Lưu và lập tiếp phiếu mới' },
      { keys: ['Ctrl', 'Q'], label: 'Lưu và đóng' },
      { keys: ['Tab'], label: 'Sang ô kế tiếp trong lưới dòng' },
    ],
  },
  {
    title: 'Bảng dữ liệu',
    items: [
      { keys: ['↑', '↓'], label: 'Di chuyển giữa các dòng' },
      { keys: ['Enter'], label: 'Mở dòng đang chọn' },
      { keys: ['Space'], label: 'Tích chọn dòng đang chọn' },
      { keys: ['Home', 'End'], label: 'Về dòng đầu hoặc dòng cuối' },
    ],
  },
]

export function ShortcutsHelp({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <Modal open={open} onClose={onClose} title="Phím tắt" size="sm">
      <div className="flex flex-col gap-4">
        {GROUPS.map((group) => (
          <section key={group.title}>
            <h3 className="mb-1.5 text-xs font-semibold text-ink-2">{group.title}</h3>
            <ul className="divide-y divide-line-2">
              {group.items.map((item) => (
                <li key={item.label} className="flex items-center gap-3 py-1.5 text-sm">
                  <span className="flex-1 text-ink">{item.label}</span>
                  <span className="flex items-center gap-1">
                    {item.keys.map((key) => (
                      <Kbd key={key}>{key}</Kbd>
                    ))}
                  </span>
                </li>
              ))}
            </ul>
          </section>
        ))}
      </div>
    </Modal>
  )
}
