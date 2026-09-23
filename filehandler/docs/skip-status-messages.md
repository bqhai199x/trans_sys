# Status, skip và message

[Trang bắt đầu](README.md) · [Metadata schema](api.md#filemetadata) · [Validation & error handling](runtime.md#validation--error-handling)

## Status

| `metadata.status` | Hành vi | HTTP |
|---|---|---|
| `success` | Hoàn tất, không có warning; vẫn có thể có skip info. | 200 |
| `partial` | Hoàn tất với warning; vùng tương ứng được giữ nguồn. | 200 |
| `failed` | Lỗi fatal trong `errors`; không có file/units attachment. | Theo lỗi trong runtime. |

`ProcessingStatus.Resolve()` ưu tiên failed, sau đó warning, rồi success. Discovery hiện trả success và `skipped: []` khi đọc inventory thành công; không chạy extraction để tạo skip nội dung.

## `SkipMetadata` và `SkipCounts`

| Property JSON | Ý nghĩa |
|---|---|
| `code` | Lý do giữ nguồn, từ `SkipCodes`. |
| `severity` | `info` cho policy/selection; `warning` cho nội dung hoặc bản dịch không xử lý được. |
| `stage` | `selection`, `extraction`, `translation`, `rename`. |
| `scope` | Loại đối tượng: `sheet`, `slide`, `row`, `cell`, `shape`, `block`, `inline`, `contentControl`, `story`, `region`, `unit`. |
| `count` | Số đối tượng mà entry đại diện. |
| `message` | Giải thích bằng tiếng Anh; nhiều message có thể dùng cùng code. |
| `location` | Tọa độ nguồn phù hợp format. |
| `unitIndex` | Index từ 0 khi đối tượng đã thành unit; bỏ property khi null. |

`skipCount: { "warning": ..., "info": ... }` cộng `count` theo severity **trước** khi lọc info. Đây là số đối tượng theo scope, không phải số unit. Khi lỗi, chỉ có số đếm đã thu thập đến thời điểm đó.

`FileMetadata.ForResponse(debug)` giữ warning trong mọi chế độ; chỉ Import `debug=true` trả thêm chi tiết info. Export dùng `false`. `OfficeSkipCollector` vẫn giữ số đếm và facts cần validation khi không tạo chi tiết info public. `MaxErrors` không cắt danh sách recoverable skip.

## Các nhóm skip chính

Bảng gom theo hành vi; các code đầy đủ nằm trong [SkipCodes.cs](../src/FileHandler.Api/Common/SkipCodes.cs), message nằm trong [ProcessingMessages.cs](../src/FileHandler.Api/Common/ProcessingMessages.cs).

| Nhóm | Code tiêu biểu | Behavior / location |
|---|---|---|
| Selection | `sheet_not_selected`, `slide_not_selected` | Info; giữ sheet/slide ngoài selection. Nhánh PowerPoint thiếu shape tree cũng dùng `slide_not_selected`. |
| Worksheet không hỗ trợ | `unsupported_sheet` | Warning khi sheet được chọn không phải worksheet. |
| Cell theo policy | `hidden_row`, `hidden_column`, `protected_table_cell`, `formula_cell`, `non_text_cell`, `whitespace_cell` | Info; giữ hàng/ô Excel tương ứng. `hidden_column` được ghi theo từng cell. |
| Cell chưa xử lý | `implicit_cell_address`, `phonetic_content`, `merged_follower_text` | Warning; giữ vùng cell nguồn. |
| Drawing | `unsupported_graphic_frame` | Warning trong Excel/PowerPoint; các unit độc lập vẫn được xử lý. |
| Word exclusion | `protected_content_control`, `unsupported_revision`, `unsupported_ruby`, `unsupported_altchunk`, `unsupported_alternate_content` | Warning; `WordExclusions` giữ subtree tương ứng. |
| Word field/story | `cross_paragraph_field`, `unbounded_field_story`, `unreferenced_story`, `system_note` | Giữ paragraph/story; hai code cuối là info. |
| Markdown block | `protected_code_block`, `protected_block` | Info; giữ code/HTML/front matter được nhận diện. |
| Mermaid | `unsupported_mermaid` | Warning nếu fenced Mermaid không extract được label. |
| Inline bảo vệ | `protected_inline` | Info; giữ inline tương ứng. |
| Bản dịch | `empty_translation`, `invalid_translation` | Warning của unit nếu có thể giữ nguồn; message ví dụ `Empty translation; source retained.` |
| Token | `invalid_marker_syntax`, `office_token_mismatch` | Warning; token thiếu/sai không được apply. |
| Markdown structure | `internal_anchor_change_unsupported`, `invalid_structure` | Warning khi định vị được unit; giữ heading/block nguồn. |
| Excel rename | `unsafe_sheet_reference` | Warning; giữ tên sheet nguồn, tiếp tục text hợp lệ. |

`invalid_translation` với null khi gọi Service trực tiếp là fatal; qua HTTP, parser đã từ chối phần tử null bằng `invalid_texts`. `invalid_structure` cũng có thể nằm trong `errors` khi validation toàn tài liệu thất bại. Phân biệt bằng vị trí trong response: `errors` hoặc `metadata.skipped`.

Nguồn xử lý: [FileMetadata.cs](../src/FileHandler.Api/Common/FileMetadata.cs), [ProcessingStatus.cs](../src/FileHandler.Api/Common/ProcessingStatus.cs), [SkipCounts.cs](../src/FileHandler.Api/Common/SkipCounts.cs), [OfficeSkipCollector.cs](../src/FileHandler.Api/Modules/Office/OfficeSkipCollector.cs). Rule cụ thể theo format: [processing.md](processing.md).
