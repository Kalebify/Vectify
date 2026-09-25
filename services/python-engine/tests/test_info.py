def test_info_returns_service_version_and_capabilities(client):
    response = client.get("/api/v1/info")

    assert response.status_code == 200
    body = response.json()
    assert body["service"]
    assert body["version"]
    assert isinstance(body["capabilities"], list)
    assert "health-check" in body["capabilities"]


def test_info_capabilities_include_every_real_pipeline_stage(client):
    # Guarda contra que /api/v1/info vuelva a quedar desactualizado (quedó
    # anunciando solo "health-check" entre M1-S03 y M1-S08 porque ningún
    # sprint lo tocó al agregar su propio endpoint) -- cada etapa real del
    # pipeline debe estar declarada acá.
    response = client.get("/api/v1/info")

    body = response.json()
    for capability in ("preprocess", "threshold", "vectorize", "simplify", "check"):
        assert capability in body["capabilities"]
