"""Logging estructurado (JSON por línea) para el motor Python."""

import logging
import sys


def configure_logging(level: str = "info") -> None:
    logging.basicConfig(
        level=level.upper(),
        format=(
            '{"timestamp":"%(asctime)s","level":"%(levelname)s",'
            '"logger":"%(name)s","message":"%(message)s"}'
        ),
        stream=sys.stdout,
        force=True,
    )
