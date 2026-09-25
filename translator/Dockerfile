FROM python:3.12-slim
WORKDIR /opt/translator
COPY pyproject.toml requirements.lock ./
COPY src ./src
RUN pip install --no-cache-dir -r requirements.lock . && useradd --system --uid 10001 translator
USER translator
ENV TRANSLATOR_CONFIG=/run/secrets/translator.json
EXPOSE 8000
CMD ["uvicorn", "translation_service.api:create_app", "--factory", "--host", "0.0.0.0", "--port", "8000", "--no-access-log"]
