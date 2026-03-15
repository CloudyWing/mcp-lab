ARG PYTHON_TAG=3.12-slim
FROM python:${PYTHON_TAG}
# Install Tesseract OCR (Traditional Chinese, Simplified Chinese, English)
RUN apt-get update && apt-get install -y --no-install-recommends \
    bash \
    tesseract-ocr \
    tesseract-ocr-chi-tra \
    tesseract-ocr-chi-sim \
    libgl1 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt
COPY server.py .
EXPOSE 8080
CMD ["python", "server.py"]
