"""Regenerate the sample documents in tests/integration/docs.

Run inside the Reader image so the same libraries produce the files:
  docker run --rm -v "$PWD/tests/integration:/w" -w /w --entrypoint python mcp-lab-mcp-reader make_sample_docs.py
"""

import pymupdf as fitz
import openpyxl
from docx import Document
from PIL import Image, ImageDraw
from pptx import Presentation
from pptx.util import Inches

OUT = "docs"

pdf = fitz.open()
for number in (1, 2):
    page = pdf.new_page()
    page.insert_text((72, 72), f"MCP Lab sample pdf page {number}")
pdf.save(f"{OUT}/sample.pdf")

docx = Document()
docx.add_paragraph("MCP Lab sample docx 中文段落")
table = docx.add_table(rows=1, cols=2)
table.rows[0].cells[0].text = "key"
table.rows[0].cells[1].text = "docx-table-value"
docx.save(f"{OUT}/sample.docx")

pptx = Presentation()
slide = pptx.slides.add_slide(pptx.slide_layouts[5])
slide.shapes.title.text = "MCP Lab sample pptx"
slide.shapes.add_textbox(Inches(1), Inches(2), Inches(4), Inches(1)).text_frame.text = "投影片內容"
pptx.save(f"{OUT}/sample.pptx")

workbook = openpyxl.Workbook()
sheet = workbook.active
sheet.title = "Data"
sheet.append(["name", "value"])
sheet.append(["MCP Lab sample xlsx", "試算表"])
workbook.save(f"{OUT}/sample.xlsx")

image = Image.new("RGB", (64, 32), "white")
ImageDraw.Draw(image).rectangle([8, 8, 56, 24], outline="red")
image.save(f"{OUT}/sample.png")
