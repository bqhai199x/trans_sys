# Token và placeholder

[Trang bắt đầu](README.md) · [Processing](processing.md) · [Status/skip](skip-status-messages.md)

## Wire format

Markdown và Office dùng token sau để giữ liên kết bản dịch với formatting/nội dung nguồn:

| Token | Ý nghĩa | Rule |
|---|---|---|
| `<ox:r0>text</ox:r0>` | Slot text được dịch. | Có thể đổi thứ tự trong cùng vùng giữa boundary; giữ ID. |
| `<ox:k0/>` | Protected anchor có thể di chuyển, ví dụ inline code. | Giữ nguyên token; chỉ di chuyển trong cùng vùng. |
| `<ox:b0/>` | Boundary hoặc đối tượng cố định, ví dụ break/container boundary. | Giữ thứ tự; token khác không được vượt qua. |

ID gồm `r`, `k` hoặc `b` và số nguyên không âm chuẩn, không có số 0 thừa. ID có thể không liên tiếp; mỗi ID nguồn xuất hiện đúng một lần trong unit kết quả. Run không lồng nhau, không có literal ngoài token. Bên trong run, escape `\` thành `\\`, `<` thành `\<`. Một slot có thể rỗng nếu unit vẫn còn text không trắng.

Text thường không chứa `<ox:` hoặc `</ox:` có thể được trả trực tiếp. Khi literal nguồn chứa prefix dành riêng, `TranslationTokenSyntax.EncodeLiteral()` bọc bằng một run và escape, kể cả TXT:

```text
Source: Use <ox:b0/> literally.
Import: <ox:r0>Use \<ox:b0/> literally.</ox:r0>
```

## Tạo và restore

| Format | Tạo token | Export restore |
|---|---|---|
| Markdown | `MarkdownExtractor.Extract()` tạo marker/template nội bộ; `MarkdownTokenCodec.Encode()` xuất token `ox`. | `MarkdownTokenCodec.Decode()` kiểm tra token, map về marker nguồn; `MarkdownTranslationApplier.Apply()` restore formatting/protected spans và escape text. |
| Word/Excel/PowerPoint | `OfficeTemplateBuilder` tạo slots, anchors, bindings; `OfficeTextCodec.Encode()` xuất token theo scope. | `OfficeTextCodec.ValidateAndDecode()` decode theo ID; applier dùng bindings và source XML để apply text/fragment. |
| TXT | `TranslationTokenSyntax.EncodeLiteral()` chỉ bọc literal có prefix dành riêng. | `PlainTextService.ExportAsync()` validate run, decode literal và nối lại source separators. |

`TranslationTokenSyntax.Encode()` chèn boundary khi chuyển scope. `Compact()` ẩn boundary đầu/cuối và rút nhóm boundary liên tiếp ở giữa còn một token; dữ liệu nguồn vẫn giữ nội bộ. `Expand()` phục hồi nhóm boundary khi restore. Template chỉ có một run và không có movable anchor có thể xuất plain text nếu không đụng prefix dành riêng.

Source file được đọc lại khi Export để dựng template; request không truyền bảng placeholder hay token metadata. Token đại diện cho nội dung nguồn, không mang toàn bộ nội dung đó trong bản dịch.

## Invalid hoặc missing token

`TranslationTokenParser.Validate()` kiểm tra syntax, tập ID và vùng boundary. Thiếu/lặp/đổi ID, escape sai hoặc chuyển token qua boundary không hợp lệ. Mã parser nội bộ như `token_syntax`, `token_identity`, `token_region`, `empty_translation` được caller chuyển thành lỗi/skip của format.

- Markdown: lỗi decode được ghi thành warning của unit, ví dụ `invalid_marker_syntax`; restore source cho unit lỗi.
- Office: token mismatch thành `office_token_mismatch`; giữ source slots cho unit lỗi.
- TXT có literal đã bọc: token sai thành `invalid_translation`, giữ paragraph nguồn.
- Quota/count/null không được chuyển thành token fallback; xem [validation](runtime.md#validation--error-handling).

## Example Markdown

Example khớp assertion trong [FilesApiTests.MarkdownWireTokensRoundTripThroughHttp()](../tests/FileHandler.Tests/Api/FilesApiTests.cs):

```text
Source
Before **red** after `code`

→ Extracted text
<ox:r0>Before </ox:r0><ox:r1>red</ox:r1><ox:r2> after </ox:r2><ox:k0/>

→ Translated text
<ox:r0>Before </ox:r0><ox:r1>đỏ</ox:r1><ox:r2> after </ox:r2><ox:k0/>

→ Output
Before **đỏ** after `code`
```

Nguồn chính: [TranslationTokenSyntax.cs](../src/FileHandler.Api/Common/TranslationTokenSyntax.cs), [TranslationTokenParser.cs](../src/FileHandler.Api/Common/TranslationTokenParser.cs), [MarkdownTokenCodec.cs](../src/FileHandler.Api/Modules/Markdown/MarkdownTokenCodec.cs), [OfficeTextCodec.cs](../src/FileHandler.Api/Modules/Office/OfficeTextCodec.cs).
